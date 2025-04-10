using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using DiscordMessageReceiver.Dtos;
using DiscordMessageReceiver.Services;
using DiscordMessageReceiver.Utils;

namespace DiscordMessageReceiver.Services.Messengers{
    public class BaseMessenger{
        protected readonly string _gameServiceBaseUrl;
        protected readonly APIRequestWrapper _apiWrapper;
        protected readonly DiscordSocketClient _client;
        public BaseMessenger(DiscordSocketClient client, APIRequestWrapper apiWrapper, string gameServiceBaseUrl)
        {
            _gameServiceBaseUrl = gameServiceBaseUrl;
            _apiWrapper = apiWrapper;
            _client = client;
        }

        protected async Task<bool> CheckUserIsAOnlineAsync(ulong userId)
        {
            var response = await _apiWrapper.GetAsync(_gameServiceBaseUrl + $"game/{userId}/status");
            if (response == null)
            {
                throw new UserErrorException($"{nameof(CheckUserIsAOnlineAsync)}: Failed to check user status");
            }

            var status = JsonSerializerWrapper.Deserialize<PlayerStatusDto>(response);
            if (status.Online)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public async Task<string> GetUserSummaryAsync(ulong userId)
        {
            var response = await _apiWrapper.GetAsync(_gameServiceBaseUrl + $"game/{userId}/summary");
            if (response == null)
            {
                throw new UserErrorException($"{nameof(GetUserSummaryAsync)}: Failed to get user summary");
            }

            var summary = response;
            if (summary == null)
            {
                throw new UserErrorException($"{nameof(GetUserSummaryAsync)}: Failed to get user summary");
            }

            return summary;
        }

        public async Task<string> GetUserMapAsync(ulong userId)
        {
            var response = await _apiWrapper.GetAsync(_gameServiceBaseUrl + $"game/{userId}/map");
            if (response == null)
            {
                throw new UserErrorException($"{nameof(GetUserMapAsync)}: Failed to get user map");
            }

            var map = response;
            if (map == null)
            {
                throw new UserErrorException($"{nameof(GetUserMapAsync)}: Failed to get user map");
            }

            return map;
        }

        public async Task<string> GetBattleSummaryAsync(ulong userId)
        {
            var response = await _apiWrapper.GetAsync(_gameServiceBaseUrl + $"battle/{userId}/summary");
            if (response == null)
            {
                throw new UserErrorException($"{nameof(GetBattleSummaryAsync)}: Failed to get battle summary");
            }

            var battleSummary = response;
            if (battleSummary == null)
            {
                throw new UserErrorException($"{nameof(GetBattleSummaryAsync)}: Failed to get battle summary");
            }

            return battleSummary;
        }

        public async Task<string> SaveGameAsync(ulong userId)
        {
            var response = await _apiWrapper.PostAsync(_gameServiceBaseUrl + $"saveload/{userId}/save");
            if (response == null)
            {
                throw new UserErrorException($"{nameof(SaveGameAsync)}: Failed to save game");
            }

            var saveGame = response;
            if (saveGame == null)
            {
                throw new UserErrorException($"{nameof(SaveGameAsync)}: Failed to save game");
            }

            return saveGame;
        }

        public async Task<string> LoadGameAsync(ulong userId)
        {
            var response = await _apiWrapper.GetAsync(_gameServiceBaseUrl + $"saveload/{userId}/load");
            if (response == null)
            {
                throw new UserErrorException($"{nameof(LoadGameAsync)}: Failed to load game");
            }

            var loadGame = response;
            if (loadGame == null)
            {
                throw new UserErrorException($"{nameof(LoadGameAsync)}: Failed to load game");
            }

            return loadGame;
        }

        protected async Task SendMessageAsync(ulong userId, string message, ComponentBuilder? component=null, bool formatted=false)
        {
            // if (!await CheckUserIsAOnlineAsync(userId))
            // {
            //     return;
            // }
            var user = await _client.Rest.GetUserAsync(userId);
            string formattedMessage = message;
            if (formatted)
            {
                formattedMessage = $"```\n{message}\n```";
            }
            if (user == null)
            {
                throw new UserErrorException($"{nameof(SendMessageAsync)}: Failed to send message to user");
            }

            var dm = await user.CreateDMChannelAsync();

            if (component == null)
            {
                await dm.SendMessageAsync(formattedMessage);
            }else
            {
                await dm.SendMessageAsync(formattedMessage, components: component.Build());
            }
        }

        public async Task<string> GetPlayerGameStateAsync(ulong userId)
        {
            var response = await _apiWrapper.GetAsync(_gameServiceBaseUrl + $"game/{userId}/state");
            if (response == null)
            {
                throw new UserErrorException($"{nameof(GetPlayerGameStateAsync)}: Failed to get user game state");
            }
            var gameState = response;
            if (gameState == null)
            {
                throw new UserErrorException($"{nameof(GetPlayerGameStateAsync)}: Failed to get user game state");
            }
            return gameState;
        }

        public async Task EnterDungeonAsync(ulong userId)
        {
            var response = await _apiWrapper.GetAsync(_gameServiceBaseUrl + $"game/{userId}/map/enter");
            if (response == null)
            {
                throw new UserErrorException($"{nameof(EnterDungeonAsync)}: Failed to enter dungeon");
            }

            var dungeon = response;
            if (dungeon == null)
            {
                throw new UserErrorException($"{nameof(EnterDungeonAsync)}: Failed to enter dungeon");
            }

            await SendMessageAsync(userId, dungeon);
            await SendMessageAsync(userId, await GetUserSummaryAsync(userId));
            await StartExplorationAsync(userId);
        }

        public async Task ContiueExplorationAsync(ulong userId)
        {
            var response = await _apiWrapper.GetAsync(_gameServiceBaseUrl + $"game/{userId}/map/neighbors");
            var directions = JsonSerializerWrapper.Deserialize<string[]>(response);
            if (directions == null || directions.Length == 0)
            {
                throw new UserErrorException($"{nameof(ContiueExplorationAsync)}: Failed to get user map directions");
            }
            
            var component = new ComponentBuilder();

            foreach (var direction in directions)
            {
                string label = string.Empty;
                string id = "adventure_" + direction;
                switch (direction)
                {
                    case "up":
                        label = "⬆️ Up";
                        break;
                    case "down":
                        label = "⬇️ Down";
                        break;
                    case "left":
                        label = "⬅️ Left";
                        break;
                    case "right":
                        label = "➡️ Right";
                        break;
                }
                component.WithButton(label, id, ButtonStyle.Primary);
            }

            await SendMessageAsync(userId, await GetUserMapAsync(userId));
            await SendMessageAsync(userId, "🏰 **Choose a room to enter:**\nSelect one of the available rooms below.", component);
        }

        /// <summary>
        /// 유저에게 버튼이 포함된 배틀 상태 선택지 메시지를 DM으로 보냅니다.
        /// </summary>
        public async Task ContiueBattleAsync(ulong userId)
        {
            await SendMessageAsync(userId, await GetBattleSummaryAsync(userId));
            await SendMessageAsync(userId, "⚔️ What would you like to do?", new ComponentBuilder()
                .WithButton("⚔ Attack", "battle_attack", ButtonStyle.Primary)
                .WithButton("🏃 Run", "battle_run", ButtonStyle.Danger));
        }

        public async Task StartMainStateAsync(ulong userId)
        {
            await SendMessageAsync(userId, "🎮 What would you like to do?", new ComponentBuilder()
                .WithButton("▶ Continue Game", "game_continue_game", ButtonStyle.Primary)
                .WithButton("🆕 New Game", "game_new_game", ButtonStyle.Success)
                .WithButton("🛑 Quit Game", "game_quit_game", ButtonStyle.Danger));
        }

        public async Task StartExplorationAsync(ulong userId)
        {
            string message = $@"
            🏰 You are entering the dungeon!

            The gate creaks open...  
            Darkness and danger await beyond.

            🗺️ Your adventure begins now!
            ".Trim();
            await SendMessageAsync(userId, message);
            await ContiueExplorationAsync(userId);
        }

        public async Task StartBattleAsync(ulong userId)
        {
            string message = $@"
            ⚠️ A wild 🐉 monster appears!

            It blocks your path with a menacing glare...  
            Prepare for battle!
            ".Trim();
            await SendMessageAsync(userId, message);
            await ContiueBattleAsync(userId);
        }

        public virtual Task OnButtonExecutedAsync(SocketMessageComponent interaction){
            // 기본 동작 또는 비워도 됨
            return Task.CompletedTask;
        }
    }
}