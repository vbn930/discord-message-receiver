using Discord;
using Discord.WebSocket;

using DiscordMessageReceiver.Dtos;
using DiscordMessageReceiver.Utils;

namespace DiscordMessageReceiver.Services.Messengers{
    public class BattleMessenger : BaseMessenger
    {
        private readonly IConfiguration _configuration;
        private readonly Logger _logger;
        public BattleMessenger(DiscordSocketClient client, APIRequestWrapper apiWrapper, string gameServiceBaseUrl, IConfiguration configuration) : base(client, apiWrapper, gameServiceBaseUrl)
        {
            _configuration = configuration;
            if (null == _configuration)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            string serviceName = configuration["Logging:ServiceName"];
            _logger = new Logger(serviceName);
        }

        public async Task<bool> BossClearedAsync(ulong userId)
        {
            var response = await _apiWrapper.GetAsync(_gameServiceBaseUrl + $"battle/{userId}/boss");
            if (response == null)
            {
                await SendMessageAsync(userId, null);
                throw new UserErrorException($"{nameof(BossClearedAsync)}: Failed to check boss cleared status");
            }

            var status = JsonSerializerWrapper.Deserialize<bool>(response);

            return status;
        }
        public async Task<bool> MonsterExistsAsync(ulong userId)
        {
            var response = await _apiWrapper.GetAsync(_gameServiceBaseUrl + $"battle/{userId}/monster");
            if (response == null)
            {
                await SendMessageAsync(userId, null);
                throw new UserErrorException($"{nameof(MonsterExistsAsync)}: Failed to check monster existence");
            }

            var status = JsonSerializerWrapper.Deserialize<bool>(response);

            return status;
        }

        public async Task<string> AttackAsync(ulong userId, bool skillUsed)
        {
            var response = await _apiWrapper.PostAsync(_gameServiceBaseUrl + $"battle/{userId}/attack?skillUsed={skillUsed.ToString().ToLower()}");
            if (response == null)
            {
                await SendMessageAsync(userId, null);
                throw new UserErrorException($"{nameof(AttackAsync)}: Failed to attack");
            }

            var result = response;
            if (result == null)
            {
                throw new UserErrorException($"{nameof(AttackAsync)}: Failed to attack");
            }

            return result;
        }

        public async Task<string> MonsterAttackAsync(ulong userId)
        {
            var response = await _apiWrapper.GetAsync(_gameServiceBaseUrl + $"battle/{userId}/monster-attack");
            if (response == null)
            {
                await SendMessageAsync(userId, null);
                throw new UserErrorException($"{nameof(MonsterAttackAsync)}: Failed to get monster attack result");
            }

            var result = response;
            if (result == null)
            {
                throw new UserErrorException($"{nameof(MonsterAttackAsync)}: Failed to get monster attack result");
            }

            return result;
        }

        public async Task<BattleEscapeResultDto> RunAwayAsync(ulong userId)
        {
            var response = await _apiWrapper.GetAsync(_gameServiceBaseUrl + $"battle/{userId}/run");
            if (response == null)
            {
                await SendMessageAsync(userId, null);
                throw new UserErrorException($"{nameof(RunAwayAsync)}: Failed to run away");
            }

            var result = JsonSerializerWrapper.Deserialize<BattleEscapeResultDto>(response);
            if (result == null)
            {
                throw new UserErrorException($"{nameof(RunAwayAsync)}: Failed to run away");
            }

            return result;
        }

        public async Task SendAttackChoiceButtonsAsync(ulong userId)
        {
            await SendMessageAsync(userId, "⚔️ What type of attack would you like to use?", new ComponentBuilder()
                .WithButton("🗡 Normal Attack", "battle_normal_attack", ButtonStyle.Primary)
                .WithButton("✨ Skill Attack", "battle_skill_attack", ButtonStyle.Success));
        }

        public override async Task OnButtonExecutedAsync(SocketMessageComponent interaction)
        {
            await interaction.DeferAsync();

            _ = Task.Run(async () =>
            {
                using (var log = _logger.StartMethod(nameof(OnButtonExecutedAsync)))
                {
                    try
                    {
                        var user = interaction.User;
                        var customId = interaction.Data.CustomId;

                        log.SetAttribute("button.type", nameof(BattleMessenger));
                        log.SetAttribute("button.userId", user.Id.ToString());
                        log.SetAttribute("button.customId", customId);

                        string content = customId switch
                        {
                            "battle_attack"         => "⚔ You have selected **Attack**.\nPreparing your weapon...",
                            "battle_run"            => "🏃 You have selected **Run**.\nAttempting to escape...",
                            "battle_normal_attack"  => "🗡 You have selected **Normal Attack**.\nReady to strike!",
                            "battle_skill_attack"   => "✨ You have selected **Skill Attack**.\nUnleashing your special ability!",
                            _                       => $"❌ You have selected an unknown option: **{customId}**.\nPlease try again."
                        };

                        await interaction.ModifyOriginalResponseAsync(msg =>
                        {
                            msg.Content = content;
                            msg.Components = new ComponentBuilder().Build();
                        });

                        string result = string.Empty;
                        string monsterAttackResult;
                        bool monsterAttack = false;

                        switch (customId)
                        {
                            case "battle_attack":
                                await SendAttackChoiceButtonsAsync(user.Id);
                                break;
                            case "battle_run":
                                await SendMessageAsync(user.Id, "🏃 You are attempting to escape the battle.");
                                var escapeResult = await RunAwayAsync(user.Id);
                                result = escapeResult.Message;
                                monsterAttack = !escapeResult.IsEscaped;
                                break;
                            case "battle_normal_attack":
                                await SendMessageAsync(user.Id, "🗡 You are using a normal attack.");
                                result = await AttackAsync(user.Id, false);
                                monsterAttack = await MonsterExistsAsync(user.Id);
                                break;
                            case "battle_skill_attack":
                                await SendMessageAsync(user.Id, "✨ You are using a skill attack.");
                                result = await AttackAsync(user.Id, true);
                                monsterAttack = await MonsterExistsAsync(user.Id);
                                break;
                            default:
                                await SendMessageAsync(user.Id, "❌ Unknown action.");
                                break;
                        }

                        if (customId != "battle_attack")
                        {
                            await SendEmbededMessageAsync(user.Id, "🎯 You Aim and Attack!", result, Color.Blue);

                            if (monsterAttack)
                            {
                                monsterAttackResult = await MonsterAttackAsync(user.Id);
                                await SendEmbededMessageAsync(user.Id, "🩸 The Beast Leaves Its Mark.", monsterAttackResult, Color.Red);
                            }

                            var gameState = await GetPlayerGameStateAsync(user.Id);
                            switch (gameState)
                            {
                                case "MainMenuState":
                                    string message = $@"
                                    ☠️ The monster’s blow was fatal...  
                                    You fall, but legends never die.  
                                    🌟 A new destiny awaits — your story starts again.
                                    ".Trim();
                                    await SendEmbededMessageAsync(user.Id, "☠️ You Died", message, Color.DarkRed);
                                    await StartMainStateAsync(user.Id);
                                    break;
                                case "ExplorationState":
                                    bool bossCleared = await BossClearedAsync(user.Id);
                                    if (bossCleared)
                                    {
                                        await SendMessageAsync(user.Id, "🏰 You have cleared the boss and now entering to the new dungeon.");
                                        await EnterDungeonAsync(user.Id);
                                    }
                                    else
                                    {
                                        await ContiueExplorationAsync(user.Id);
                                    }
                                    break;
                                case "BattleState":
                                    await ContiueBattleAsync(user.Id);
                                    break;
                                default:
                                    await SendMessageAsync(user.Id, "❌ Unknown game state.");
                                    break;
                            }
                        }
                    }
                    catch (UserErrorException e)
                    {
                        log.LogUserError(e.Message);
                    }
                    catch (Exception e)
                    {
                        log.HandleException(e);
                    }
                }
            });
        }

    }
}
