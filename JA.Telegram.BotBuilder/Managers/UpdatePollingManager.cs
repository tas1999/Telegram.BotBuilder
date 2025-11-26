using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types.Enums;
using Telegram.BotBuilder.Contexts;
using Telegram.BotBuilder.Delegates;
using Telegram.BotBuilder.Interfaces;

namespace Telegram.BotBuilder.Managers
{
    public class UpdatePollingManager<TBot> : IUpdatePollingManager<TBot> where TBot : IBot
    {
        private readonly UpdateDelegate _updateDelegate;
        private readonly IBotServiceProvider _rootProvider;
        private readonly ILogger<UpdatePollingManager<TBot>> _logger;

        public UpdatePollingManager(IBotBuilder botBuilder, IBotServiceProvider rootProvider, ILogger<UpdatePollingManager<TBot>> logger)
        {
            _updateDelegate = botBuilder.Build();
            _rootProvider = rootProvider;
            _logger = logger;
        }

        public async Task RunAsync(
          GetUpdatesRequest? requestParams = null,
          CancellationToken cancellationToken = default)
        {
            try
            {
               var bot = (TBot)_rootProvider.GetService(typeof(TBot));

                var configuredTaskAwaitable = bot.Client.DeleteWebhookAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                await configuredTaskAwaitable;
                var getUpdatesRequest = requestParams ?? new GetUpdatesRequest()
                {
                    Offset = 0,
                    Timeout = 500,
                    AllowedUpdates = Array.Empty<UpdateType>()
                };

                requestParams = getUpdatesRequest;
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var updates = await Policy.Handle<Exception>()
                            .WaitAndRetryForeverAsync(x => TimeSpan.FromSeconds(10))
                            .ExecuteAsync(async () => await bot.Client.MakeRequestAsync(requestParams, cancellationToken)
                            .ConfigureAwait(false));
                        var updateArray = updates;
                        foreach (var u in updateArray)
                        {
                            using var scopeProvider = _rootProvider.CreateScope();
                            configuredTaskAwaitable = _updateDelegate(new UpdateContext(bot, u, scopeProvider), cancellationToken)
                                .ConfigureAwait(false);
                            await configuredTaskAwaitable;
                        }

                        updateArray = null;

                        if (updates.Length != 0)
                            requestParams.Offset = updates[^1].Id + 1;

                        updates = null;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Update while polling manager error");
                    }

                }

                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update polling manager error");
            }
        }
    }
}
