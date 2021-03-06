﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using TwitchBot.Models;

using TwitchBotDb.Models;

namespace TwitchBot.Libraries
{
    public class TimeoutCmd
    {
        public List<TimeoutUser> TimedoutUsers { get; set; } = new List<TimeoutUser>();

        public async Task<DateTime> AddTimeout(string recipient, int broadcasterId, double seconds, string twitchBotApiLink)
        {
            DateTime timeoutExpiration = DateTime.UtcNow.AddSeconds(seconds);

            UserBotTimeout timedoutUser = new UserBotTimeout();

            if (TimedoutUsers.Any(m => m.Username == recipient))
            {
                timedoutUser = await ApiBotRequest.PatchExecuteTaskAsync<UserBotTimeout>(
                    twitchBotApiLink + $"userbottimeouts/patch/{broadcasterId}?username={recipient}", 
                    "timeout", 
                    timeoutExpiration);

                TimedoutUsers.RemoveAll(t => t.Username == recipient);
            }
            else
            {
                timedoutUser = await ApiBotRequest.PostExecuteTaskAsync(
                    twitchBotApiLink + $"userbottimeouts/create",
                    new UserBotTimeout { Username = recipient, Timeout = timeoutExpiration, Broadcaster = broadcasterId }
                );
            }

            TimedoutUsers.Add(new TimeoutUser
            {
                Username = recipient,
                TimeoutExpirationUtc = timeoutExpiration,
                HasBeenWarned = false
            });

            return timeoutExpiration;
        }

        public async Task<string> DeleteUserTimeout(string recipient, int broadcasterId, string twitchBotApiLink)
        {
            UserBotTimeout removedTimeout = await ApiBotRequest.DeleteExecuteTaskAsync<UserBotTimeout>(twitchBotApiLink + $"userbottimeouts/delete/{broadcasterId}?username={recipient}");
            if (removedTimeout == null) return "";

            string name = removedTimeout.Username;

            TimedoutUsers.RemoveAll(r => r.Username == name);
            return name;
        }

        public async Task<string> GetUserTimeout(string recipient, int broadcasterId, string twitchBotApiLink)
        {
            TimeoutUser timeoutUser = TimedoutUsers.FirstOrDefault(r => r.Username == recipient);
            if (timeoutUser != null)
            {
                if (timeoutUser.TimeoutExpirationUtc < DateTime.UtcNow)
                {
                    await DeleteUserTimeout(recipient, broadcasterId, twitchBotApiLink);
                }
                else
                {
                    TimeSpan timeout = timeoutUser.TimeoutExpirationUtc - DateTime.UtcNow;
                    return timeout.ToString(@"hh\:mm\:ss");
                }
            }

            return "0 seconds"; // if cannot find timeout
        }

        public async Task<List<UserBotTimeout>> DeleteTimeouts(int broadcasterId, string twitchBotApiLink)
        {
            return await ApiBotRequest.DeleteExecuteTaskAsync<List<UserBotTimeout>>(twitchBotApiLink + $"userbottimeouts/delete/{broadcasterId}");
        }

        public async Task<List<UserBotTimeout>> GetTimeouts(int broadcasterId, string twitchBotApiLink)
        {
            return await ApiBotRequest.GetExecuteTaskAsync<List<UserBotTimeout>>(twitchBotApiLink + $"userbottimeouts/get/{broadcasterId}");
        }
    }
}
