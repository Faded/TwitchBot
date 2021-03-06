﻿using System.Threading.Tasks;

using TwitchBot.Libraries;

using TwitchBotDb.Models;

namespace TwitchBot.Repositories
{
    public class GameDirectoryRepository
    {
        private readonly string _twitchBotApiLink;

        public GameDirectoryRepository(string twitchBotApiLink)
        {
            _twitchBotApiLink = twitchBotApiLink;
        }

        public async Task<GameList> GetGameId(string gameTitle)
        {
            return await ApiBotRequest.GetExecuteTaskAsync<GameList>(_twitchBotApiLink + $"gamelists/get/{gameTitle}");
        }
    }
}
