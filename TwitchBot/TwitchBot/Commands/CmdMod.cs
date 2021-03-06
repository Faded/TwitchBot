﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using RestSharp;

using TwitchBot.Configuration;
using TwitchBot.Extensions;
using TwitchBot.Libraries;
using TwitchBot.Services;
using TwitchBot.Models.JSON;

using TwitchBotDb.DTO;
using TwitchBotDb.Models;

namespace TwitchBot.Commands
{
    public class CmdMod
    {
        private IrcClient _irc;
        private TimeoutCmd _timeout;
        private System.Configuration.Configuration _appConfig;
        private TwitchBotConfigurationSection _botConfig;
        private int _broadcasterId;
        private BankService _bank;
        private TwitchInfoService _twitchInfo;
        private ManualSongRequestService _manualSongRequest;
        private QuoteService _quote;
        private PartyUpService _partyUp;
        private GameDirectoryService _gameDirectory;
        private ErrorHandler _errHndlrInstance = ErrorHandler.Instance;
        private ModeratorSingleton _modInstance = ModeratorSingleton.Instance;
        private TwitterClient _twitter = TwitterClient.Instance;
        private BroadcasterSingleton _broadcasterInstance = BroadcasterSingleton.Instance;
        private TwitchChatterList _twitchChatterListInstance = TwitchChatterList.Instance;

        public CmdMod(IrcClient irc, TimeoutCmd timeout, TwitchBotConfigurationSection botConfig, int broadcasterId, 
            System.Configuration.Configuration appConfig, BankService bank, TwitchInfoService twitchInfo, ManualSongRequestService manualSongRequest,
            QuoteService quote, PartyUpService partyUp, GameDirectoryService gameDirectory)
        {
            _irc = irc;
            _timeout = timeout;
            _botConfig = botConfig;
            _broadcasterId = broadcasterId;
            _appConfig = appConfig;
            _bank = bank;
            _twitchInfo = twitchInfo;
            _manualSongRequest = manualSongRequest;
            _quote = quote;
            _partyUp = partyUp;
            _gameDirectory = gameDirectory;
        }

        /// <summary>
        /// Displays Discord link (if available)
        /// </summary>
        public async void CmdDiscord()
        {
            try
            {
                if (string.IsNullOrEmpty(_botConfig.DiscordLink) || _botConfig.DiscordLink.Equals("Link unavailable at the moment"))
                    _irc.SendPublicChatMessage("Discord link unavailable at the moment");
                else
                    _irc.SendPublicChatMessage("Wanna kick it with some awesome peeps like myself? Of course you do! Join this fantastic Discord! " + _botConfig.DiscordLink);
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdDiscord()", false, "!discord");
            }
        }

        /// <summary>
        /// Takes money away from a user
        /// </summary>
        /// <param name="message">Chat message from the user</param>
        /// <param name="username">User that sent the message</param>
        public async Task CmdCharge(string message, string username)
        {
            try
            {
                if (message.StartsWith("!charge @"))
                    _irc.SendPublicChatMessage($"Please enter a valid amount @{username}");
                else
                {
                    int indexAction = 8;
                    int fee = -1;
                    bool isValidFee = int.TryParse(message.Substring(indexAction, message.IndexOf("@") - indexAction - 1), out fee);
                    string recipient = message.Substring(message.IndexOf("@") + 1).ToLower();
                    int wallet = await _bank.CheckBalance(recipient, _broadcasterId);

                    // Check user's bank account exist or has currency
                    if (wallet == -1)
                        _irc.SendPublicChatMessage("The user '" + recipient + "' is not currently banking with us @" + username);
                    else if (wallet == 0)
                        _irc.SendPublicChatMessage("'" + recipient + "' is out of " + _botConfig.CurrencyType + " @" + username);
                    // Check if fee can be accepted
                    else if (fee > 0)
                        _irc.SendPublicChatMessage("Please insert a negative whole amount (no decimal numbers) "
                            + " or use the !deposit command to add " + _botConfig.CurrencyType + " to a user's account");
                    else if (!isValidFee)
                        _irc.SendPublicChatMessage("The fee wasn't accepted. Please try again with negative whole amount (no decimals)");
                    else /* Deduct funds from wallet */
                    {
                        wallet += fee;

                        // Zero out account balance if user is being charged more than they have
                        if (wallet < 0)
                            wallet = 0;

                        await _bank.UpdateFunds(recipient, _broadcasterId, wallet);

                        // Prompt user's balance
                        if (wallet == 0)
                            _irc.SendPublicChatMessage("Charged " + fee.ToString().Replace("-", "") + " " + _botConfig.CurrencyType + " to " + recipient
                                + "'s account! They are out of " + _botConfig.CurrencyType + " to spend");
                        else
                            _irc.SendPublicChatMessage("Charged " + fee.ToString().Replace("-", "") + " " + _botConfig.CurrencyType + " to " + recipient
                                + "'s account! They only have " + wallet + " " + _botConfig.CurrencyType + " to spend");
                    }
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdCharge(string, string)", false, "!charge");
            }
        }

        /// <summary>
        /// Gives a set amount of stream currency to user
        /// </summary>
        /// <param name="message">Chat message from the user</param>
        /// <param name="username">User that sent the message</param>
        public async Task CmdDeposit(string message, string username)
        {
            try
            {
                List<string> userList = new List<string>();

                foreach (int index in message.AllIndexesOf("@"))
                {
                    int lengthUsername = message.IndexOf(" ", index) - index - 1;
                    if (lengthUsername < 0)
                        userList.Add(message.Substring(index + 1).ToLower());
                    else
                        userList.Add(message.Substring(index + 1, lengthUsername).ToLower());
                }

                // Check for valid command
                if (message.StartsWith("!deposit @"))
                    _irc.SendPublicChatMessage("Please enter a valid amount to a user @" + username);
                // Check if moderator is trying to give money to themselves
                else if (_modInstance.Moderators.Contains(username.ToLower()) && userList.Contains(username.ToLower()))
                    _irc.SendPublicChatMessage($"Entire deposit voided. You cannot add funds to your own account @{username}");
                else
                {
                    int indexAction = 9;
                    int deposit = -1;
                    bool isValidDeposit = int.TryParse(message.Substring(indexAction, message.IndexOf("@") - indexAction - 1), out deposit);

                    // Check if deposit amount is valid
                    if (deposit < 0)
                        _irc.SendPublicChatMessage("Please insert a positive whole amount (no decimals) " 
                            + " or use the !charge command to remove " + _botConfig.CurrencyType + " from a user");
                    else if (!isValidDeposit)
                        _irc.SendPublicChatMessage("The deposit wasn't accepted. Please try again with a positive whole amount (no decimals)");
                    else
                    {
                        if (userList.Count > 0)
                        {
                            List<BalanceResult> balResultList = await _bank.UpdateCreateBalance(userList, _broadcasterId, deposit, true);

                            string responseMsg = $"Gave {deposit.ToString()} {_botConfig.CurrencyType} to ";

                            if (balResultList.Count > 1)
                            {
                                foreach (BalanceResult userResult in balResultList)
                                    responseMsg += $"{userResult.Username}, ";

                                responseMsg = responseMsg.ReplaceLastOccurrence(", ", "");
                            }
                            else if (balResultList.Count == 1)
                            {
                                responseMsg += $"@{balResultList[0].Username} ";

                                if (balResultList[0].ActionType.Equals("UPDATE"))
                                    responseMsg += $"and now has {balResultList[0].Wallet} {_botConfig.CurrencyType}!";
                                else if (balResultList[0].ActionType.Equals("INSERT"))
                                    responseMsg += $"and can now gamble it all away! Kappa";
                            }
                            else
                                responseMsg = $"Unknown error has occurred in retrieving results. Please check your recipient's {_botConfig.CurrencyType}";

                            _irc.SendPublicChatMessage(responseMsg);
                        }
                        else
                        {
                            _irc.SendPublicChatMessage($"There are no chatters to deposit {_botConfig.CurrencyType} @{username}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdDeposit(string, string)", false, "!deposit");
            }
        }

        /// <summary>
        /// Gives every viewer currently watching a set amount of currency
        /// </summary>
        /// <param name="message"></param>
        /// <param name="username"></param>
        public async Task CmdBonusAll(string message, string username)
        {
            try
            {
                // Check for valid command
                if (message.StartsWith("!bonusall @"))
                    _irc.SendPublicChatMessage("Please enter a valid amount to a user @" + username);
                else
                {
                    int indexAction = 10;
                    int deposit = -1;
                    bool isValidDeposit = int.TryParse(message.Substring(indexAction), out deposit);

                    // Check if deposit amount is valid
                    if (deposit < 0)
                        _irc.SendPublicChatMessage("Please insert a positive whole amount (no decimals) "
                            + " or use the !charge command to remove " + _botConfig.CurrencyType + " from a user");
                    else if (!isValidDeposit)
                        _irc.SendPublicChatMessage("The bulk deposit wasn't accepted. Please try again with positive whole amount (no decimals)");
                    else
                    {
                        // Wait until chatter lists are available
                        while (!_twitchChatterListInstance.AreListsAvailable)
                        {

                        }

                        List<string> chatterList = _twitchChatterListInstance.ChattersByName;

                        // exclude bot and the moderator/broadcaster executing this command
                        chatterList = chatterList.Where(t => t != username.ToLower() && t != _botConfig.BotName.ToLower()).ToList();

                        if (chatterList != null && chatterList.Count > 0)
                        {
                            await _bank.UpdateCreateBalance(chatterList, _broadcasterId, deposit);
                            _irc.SendPublicChatMessage($"{deposit.ToString()} {_botConfig.CurrencyType} for everyone! "
                                + $"Check your stream bank account with !{_botConfig.CurrencyType.ToLower()}");
                        }
                        else
                        {
                            _irc.SendPublicChatMessage($"There are no chatters to deposit {_botConfig.CurrencyType} @{username}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdBonusAll(string, string)", false, "!bonusall");
            }
        }

        /// <summary>
        /// Removes the first song in the queue of song requests
        /// </summary>
        public async Task CmdPopManualSr()
        {
            try
            {
                SongRequests removedSong = await _manualSongRequest.PopSongRequest(_broadcasterId);

                if (removedSong != null)
                    _irc.SendPublicChatMessage($"The first song in the queue, \"{removedSong.Requests}\" ({removedSong.Chatter}), has been removed");
                else
                    _irc.SendPublicChatMessage("There are no songs that can be removed from the song request list");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdPopManualSr()", false, "!poprbsr");
            }
        }

        /// <summary>
        /// Resets the song request queue
        /// </summary>
        public async Task CmdResetManualSr()
        {
            try
            {
                List<SongRequests> removedSong = await _manualSongRequest.ResetSongRequests(_broadcasterId);

                if (removedSong != null && removedSong.Count > 0)
                    _irc.SendPublicChatMessage($"The song request queue has been reset @{_botConfig.Broadcaster}");
                else
                    _irc.SendPublicChatMessage($"Song requests are empty @{_botConfig.Broadcaster}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdResetManualSr()", false, "!resetrbsr");
            }
        }

        /// <summary>
        /// Removes first party memeber in queue of party up requests
        /// </summary>
        public async Task CmdPopPartyUpRequest()
        {
            try
            {
                // get current game info
                ChannelJSON json = await _twitchInfo.GetBroadcasterChannelById();
                string gameTitle = json.Game;
                GameList game = await _gameDirectory.GetGameId(gameTitle);

                _irc.SendPublicChatMessage(await _partyUp.PopRequestedPartyMember(game.Id, _broadcasterId));
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdPopPartyUpRequest()", false, "!poppartyuprequest");
            }
        }

        /// <summary>
        /// Bot-specific timeout on a user for a set amount of time
        /// </summary>
        /// <param name="message">Chat message from the user</param>
        /// <param name="username">User that sent the message</param>
        public async Task CmdAddTimeout(string message, string username)
        {
            try
            {
                if (message.StartsWith("!timeout @"))
                    _irc.SendPublicChatMessage("I cannot make a user not talk to me without this format '!timeout [seconds] @[username]'");
                else if (message.ToLower().Contains(_botConfig.Broadcaster.ToLower()))
                    _irc.SendPublicChatMessage("I cannot betray @" + _botConfig.Broadcaster + " by not allowing him to communicate with me @" + username);
                else if (message.ToLower().Contains(_botConfig.BotName.ToLower()))
                    _irc.SendPublicChatMessage("You can't time me out @" + username);
                else
                {
                    int indexAction = 9;
                    string recipient = message.Substring(message.IndexOf("@") + 1).ToLower();
                    double seconds = -1;
                    bool isValidTimeout = double.TryParse(message.Substring(indexAction, message.IndexOf("@") - indexAction - 1), out seconds);

                    if (!isValidTimeout || seconds < 0.00)
                        _irc.SendPublicChatMessage("The timeout amount wasn't accepted. Please try again with positive seconds only");
                    else if (seconds < 15.00)
                        _irc.SendPublicChatMessage("The duration needs to be at least 15 seconds long. Please try again");
                    else
                    {
                        DateTime timeoutExpiration = await _timeout.AddTimeout(recipient, _broadcasterId, seconds, _botConfig.TwitchBotApiLink);

                        _irc.SendPublicChatMessage($"I'm told not to talk to you until {timeoutExpiration.ToLocalTime()} ({TimeZone.CurrentTimeZone.StandardName}) @{recipient}");
                    }
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdAddTimeout(string, string)", false, "!timeout");
            }
        }

        /// <summary>
        /// Remove bot-specific timeout on a user for a set amount of time
        /// </summary>
        /// <param name="message">Chat message from the user</param>
        /// <param name="username">User that sent the message</param>
        public async Task CmdDelTimeout(string message, string username)
        {
            try
            {
                string recipient = message.Substring(message.IndexOf("@") + 1).ToLower();

                recipient = await _timeout.DeleteUserTimeout(recipient, _broadcasterId, _botConfig.TwitchBotApiLink);

                if (!string.IsNullOrEmpty(recipient))
                    _irc.SendPublicChatMessage($"{recipient} can now interact with me again because of @{username} @{_botConfig.Broadcaster}");
                else
                    _irc.SendPublicChatMessage($"Cannot find the user you wish to timeout @{username}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdDelTimeout(string, string)", false, "!deltimeout");
            }
        }

        /// <summary>
        /// Set delay for messages based on the latency of the stream
        /// </summary>
        /// <param name="message">Chat message from the user</param>
        /// <param name="username">User that sent the message</param>
        public async void CmdSetLatency(string message, string username)
        {
            try
            {
                int latency = -1;
                bool isValidInput = int.TryParse(message.Substring(12), out latency);

                if (!isValidInput || latency < 0)
                    _irc.SendPublicChatMessage("Please insert a valid positive alloted amount of time (in seconds)");
                else
                {
                    _botConfig.StreamLatency = latency;
                    _appConfig.AppSettings.Settings.Remove("streamLatency");
                    _appConfig.AppSettings.Settings.Add("streamLatency", latency.ToString());
                    _appConfig.Save(ConfigurationSaveMode.Modified);
                    ConfigurationManager.RefreshSection("TwitchBotConfiguration");

                    Console.WriteLine("Stream latency set to " + _botConfig.StreamLatency + " second(s)");
                    _irc.SendPublicChatMessage("Bot settings for stream latency set to " + _botConfig.StreamLatency + " second(s) @" + username);
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdSetLatency(string, string)", false, "!setlatency");
            }
        }

        /// <summary>
        /// Add a mod/broadcaster quote
        /// </summary>
        /// <param name="message">Chat message from the user</param>
        /// <param name="username">User that sent the message</param>
        public async Task CmdAddQuote(string message, string username)
        {
            try
            {
                string quote = message.Substring(message.IndexOf(" ") + 1);

                await _quote.AddQuote(quote, username, _broadcasterId);

                _irc.SendPublicChatMessage($"Quote has been created @{username}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdAddQuote(string, string)", false, "!addquote");
            }
        }

        /// <summary>
        /// Tell the stream the specified moderator will be AFK
        /// </summary>
        /// <param name="username">User that sent the message</param>
        public async void CmdModAfk(string username)
        {
            try
            {
                _irc.SendPublicChatMessage($"@{username} is going AFK!");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdModAfk(string)", false, "!modafk");
            }
        }

        /// <summary>
        /// Tell the stream the specified moderator is back
        /// </summary>
        /// <param name="username">User that sent the message</param>
        public async void CmdModBack(string username)
        {
            try
            {
                _irc.SendPublicChatMessage($"@{username} is back!");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdModBack(string)", false, "!modback");
            }
        }

        /// <summary>
        /// Add user(s) to a MultiStream link so viewers can watch multiple streamers at the same time
        /// </summary>
        /// <param name="message">Chat message from the user</param>
        /// <param name="username">User that sent the message</param>
        /// <param name="multiStreamUsers">List of users that have already been added to the link</param>
        public async Task<List<string>> CmdAddMultiStreamUser(string message, string username, List<string> multiStreamUsers)
        {
            try
            {
                int userLimit = 3;

                // Hard-coded limit to 4 users (including broadcaster) 
                // because of possible video bandwidth issues for users...for now
                if (multiStreamUsers.Count >= userLimit)
                    _irc.SendPublicChatMessage($"Max limit of users set for the MultiStream link! Please reset the link @{username}");
                else if (message.IndexOf("@") == -1)
                    _irc.SendPublicChatMessage($"Please use the \"@\" to define new user(s) to add @{username}");
                else if (message.Contains(_botConfig.Broadcaster, StringComparison.CurrentCultureIgnoreCase)
                            || message.Contains(_botConfig.BotName, StringComparison.CurrentCultureIgnoreCase))
                {
                    _irc.SendPublicChatMessage($"I cannot add the broadcaster or myself to the MultiStream link @{username}");
                }
                else
                {
                    List<int> indexNewUsers = message.AllIndexesOf("@");

                    if (multiStreamUsers.Count + indexNewUsers.Count > userLimit)
                        _irc.SendPublicChatMessage("Too many users are being added to the MultiStream link " + 
                            $"< Number of users already added: \"{multiStreamUsers.Count}\" >" + 
                            $"< User limit (without broadcaster): \"{userLimit}\" > @{username}");
                    else
                    {
                        string setMultiStreamUsers = "";
                        string verbUsage = "has ";

                        if (indexNewUsers.Count == 1)
                        {
                            string newUser = message.Substring(indexNewUsers[0] + 1);

                            if (!multiStreamUsers.Contains(newUser.ToLower()))
                            {
                                multiStreamUsers.Add(newUser.ToLower());
                                setMultiStreamUsers = $"@{newUser.ToLower()} ";
                            }
                            else
                            {
                                setMultiStreamUsers = $"{newUser} ";
                                verbUsage = "has already ";
                            }
                        }
                        else
                        {
                            for (int i = 0; i < indexNewUsers.Count; i++)
                            {
                                int indexNewUser = indexNewUsers[i] + 1;
                                string setMultiStreamUser = "";

                                if (i + 1 < indexNewUsers.Count)
                                    setMultiStreamUser = message.Substring(indexNewUser, indexNewUsers[i + 1] - indexNewUser - 1).ToLower();
                                else
                                    setMultiStreamUser = message.Substring(indexNewUser).ToLower();

                                if (!multiStreamUsers.Contains(setMultiStreamUser))
                                    multiStreamUsers.Add(setMultiStreamUser.ToLower());
                            }

                            foreach (string multiStreamUser in multiStreamUsers)
                                setMultiStreamUsers += $"@{multiStreamUser} ";

                            verbUsage = "have ";
                        }

                        string resultMsg = $"{setMultiStreamUsers} {verbUsage} been set up for the MultiStream link @{username}";

                        if (username.ToLower().Equals(_botConfig.Broadcaster.ToLower()))
                            _irc.SendPublicChatMessage(resultMsg);
                        else
                            _irc.SendPublicChatMessage($"{resultMsg} @{_botConfig.Broadcaster.ToLower()}");
                    }
                }

                return multiStreamUsers;
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdAddMultiStreamUser(string, string, ref List<string>)", false, "!addmsl", message);
            }

            return new List<string>();
        }

        /// <summary>
        /// Reset the MultiStream link to allow the link to be reconfigured
        /// </summary>
        /// <param name="username">User that sent the message</param>
        /// <param name="multiStreamUsers">List of users that have already been added to the link</param>
        public async Task<List<string>> CmdResetMultiStreamLink(string username, List<string> multiStreamUsers)
        {
            try
            {
                multiStreamUsers = new List<string>();

                string resultMsg = "MultiStream link has been reset. " + 
                    $"Please reconfigure the link if you are planning on using it in the near future @{username}";

                if (username.ToLower().Equals(_botConfig.Broadcaster.ToLower()))
                    _irc.SendPublicChatMessage(resultMsg);
                else
                    _irc.SendPublicChatMessage($"{resultMsg} @{_botConfig.Broadcaster.ToLower()}");

                return multiStreamUsers;
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdResetMultiStream(string, ref List<string>)", false, "!resetmsl");
            }

            return new List<string>();
        }

        /// <summary>
        /// Update the title of the Twitch channel
        /// </summary>
        /// <param name="message">Chat message from the user</param>
        public async Task CmdUpdateTitle(string message)
        {
            try
            {
                // Get title from command parameter
                string title = message.Substring(message.IndexOf(" ") + 1);

                // Send HTTP method PUT to base URI in order to change the title
                RestClient client = new RestClient("https://api.twitch.tv/kraken/channels/" + _broadcasterInstance.TwitchId);
                RestRequest request = new RestRequest(Method.PUT);
                request.AddHeader("Cache-Control", "no-cache");
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Authorization", "OAuth " + _botConfig.TwitchAccessToken);
                request.AddHeader("Accept", "application/vnd.twitchtv.v5+json");
                request.AddHeader("Client-ID", _botConfig.TwitchClientId);
                request.AddParameter("application/json", "{\"channel\":{\"status\":\"" + title + "\"}}",
                    ParameterType.RequestBody);

                IRestResponse response = null;
                try
                {
                    response = await client.ExecuteTaskAsync<Task>(request);
                    string statResponse = response.StatusCode.ToString();
                    if (statResponse.Contains("OK"))
                    {
                        _irc.SendPublicChatMessage($"Twitch channel title updated to \"{title}\"");
                    }
                    else
                        Console.WriteLine(response.ErrorMessage);
                }
                catch (WebException ex)
                {
                    if (((HttpWebResponse)ex.Response).StatusCode == HttpStatusCode.BadRequest)
                    {
                        Console.WriteLine("Error 400 detected!");
                    }
                    response = (IRestResponse)ex.Response;
                    Console.WriteLine("Error: " + response);
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdUpdateTitle(string)", false, "!updatetitle");
            }
        }

        /// <summary>
        /// Updates the game being played on the Twitch channel
        /// </summary>
        /// <param name="message">Chat message from the user</param>
        /// <param name="hasTwitterInfo">Check for Twitter credentials</param>
        public async Task CmdUpdateGame(string message, bool hasTwitterInfo)
        {
            try
            {
                // Get game from command parameter
                string game = message.Substring(message.IndexOf(" ") + 1);

                // Send HTTP method PUT to base URI in order to change the game
                RestClient client = new RestClient("https://api.twitch.tv/kraken/channels/" + _broadcasterInstance.TwitchId);
                RestRequest request = new RestRequest(Method.PUT);
                request.AddHeader("Cache-Control", "no-cache");
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Authorization", "OAuth " + _botConfig.TwitchAccessToken);
                request.AddHeader("Accept", "application/vnd.twitchtv.v5+json");
                request.AddHeader("Client-ID", _botConfig.TwitchClientId);
                request.AddParameter("application/json", "{\"channel\":{\"game\":\"" + game + "\"}}",
                    ParameterType.RequestBody);

                IRestResponse response = null;
                try
                {
                    response = await client.ExecuteTaskAsync<Task>(request);
                    string statResponse = response.StatusCode.ToString();
                    if (statResponse.Contains("OK"))
                    {
                        _irc.SendPublicChatMessage($"Twitch channel game status updated to \"{game}\"");
                        if (_botConfig.EnableTweets && hasTwitterInfo)
                        {
                            Console.WriteLine(_twitter.SendTweet($"Just switched to \"{game}\" on " 
                                + $"twitch.tv/{_broadcasterInstance.Username}"));
                        }
                    }
                    else
                    {
                        Console.WriteLine(response.Content);
                    }
                }
                catch (WebException ex)
                {
                    if (((HttpWebResponse)ex.Response).StatusCode == HttpStatusCode.BadRequest)
                    {
                        Console.WriteLine("Error 400 detected!!");
                    }
                    response = (IRestResponse)ex.Response;
                    Console.WriteLine("Error: " + response);
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdUpdateGame(string, bool)", false, "!updategame");
            }
        }

        public async Task<Queue<string>> CmdPopJoin(string username, Queue<string> gameQueueUsers)
        {
            try
            {
                if (gameQueueUsers.Count == 0)
                    _irc.SendPublicChatMessage($"Queue is empty @{username}");
                else
                {
                    string poppedUser = gameQueueUsers.Dequeue();
                    _irc.SendPublicChatMessage($"{poppedUser} has been removed from the queue @{username}");
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdPopJoin(string, Queue<string>)", false, "!popjoin");
            }

            return gameQueueUsers;
        }

        public async Task<Queue<string>> CmdResetJoin(string username, Queue<string> gameQueueUsers)
        {
            try
            {
                if (gameQueueUsers.Count != 0) gameQueueUsers.Clear();

                _irc.SendPublicChatMessage($"Queue is empty @{username}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdResetJoin(string, Queue<string>)", false, "!resetjoin");
            }

            return gameQueueUsers;
        }

        public async Task CmdPromoteStreamer(string message, string username)
        {
            try
            {
                string streamerUsername = message.Substring(message.IndexOf("@") + 1);

                RootUserJSON userInfo = await _twitchInfo.GetUsersByLoginName(streamerUsername);
                if (userInfo.Users.Count == 0)
                {
                    _irc.SendPublicChatMessage($"Cannot find the requested user @{username}");
                    return;
                }

                string userId = userInfo.Users.First().Id;
                string promotionMessage = $"Hey everyone! Check out {streamerUsername}'s channel at https://www.twitch.tv/" + streamerUsername 
                    + " and slam that follow button!";
                RootStreamJSON userStreamInfo = await _twitchInfo.GetUserStream(userId);

                if (userStreamInfo.Stream == null)
                {
                    ChannelJSON channelInfo = await _twitchInfo.GetUserChannelById(userId);

                    if (!string.IsNullOrEmpty(channelInfo.Game))
                        promotionMessage += $" They were last seen playing \"{channelInfo.Game}\"";
                }
                else
                {
                    if (!string.IsNullOrEmpty(userStreamInfo.Stream.Game))
                        promotionMessage += $" Right now, they're playing \"{userStreamInfo.Stream.Game}\"";
                }

                _irc.SendPublicChatMessage(promotionMessage);
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdResetGotNextGame(string, string)", false, "!streamer");
            }
        }
    }
}
