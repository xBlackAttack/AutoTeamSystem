using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("AutoTeamSystem", "BlackAttack", "1.1.0")]
    [Description("Automatically adds players to preconfigured teams and prevents them from leaving")]
    class AutoTeamSystem : RustPlugin
    {

        #region Configuration

        private ConfigData configData;

        class ConfigData
        {
            public List<TeamConfig> Teams { get; set; } = new List<TeamConfig>();
        }

        class TeamConfig
        {
            [JsonProperty("Team Name")]
            public string TeamName { get; set; }

            [JsonProperty("Members (SteamID)")]
            public List<ulong> MemberSteamIDs { get; set; } = new List<ulong>();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                configData = Config.ReadObject<ConfigData>();
                if (configData == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch (Exception ex)
            {
                PrintError("Failed to load configuration file: " + ex.Message);
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file...");
            configData = new ConfigData
            {
                Teams = new List<TeamConfig>
                {
                    new TeamConfig
                    {
                        TeamName = "Team #6014",
                        MemberSteamIDs = new List<ulong> { 0, 1, 2, 3 }
                    },
                    new TeamConfig
                    {
                        TeamName = "Team #1670",
                        MemberSteamIDs = new List<ulong> { 0, 1, 2, 3 }
                    },
                    new TeamConfig
                    {
                        TeamName = "Team #5076",
                        MemberSteamIDs = new List<ulong> { 0, 1, 2, 3 }
                    }
                }
            };
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(configData);
        }

        #endregion

        #region Oxide Hooks

        private void OnServerInitialized()
        {
            LoadConfig();
            timer.Every(300f, () => CheckAllTeams());
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            timer.Once(5f, () => AssignPlayerToTeam(player));
        }

        private void OnTeamLeave(RelationshipManager.PlayerTeam team, BasePlayer player)
        {
            // Blocking exit from the team
            timer.Once(0.5f, () => AssignPlayerToTeam(player));
            SendReply(player, "You cannot leave the team. You are automatically added again.");
        }

        #endregion

        #region Core Methods

        private void AssignPlayerToTeam(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
                return;

            ulong steamID = player.userID;
            
            // Check which team the player belongs to
            TeamConfig playerTeam = null;
            foreach (var team in configData.Teams)
            {
                if (team.MemberSteamIDs.Contains(steamID))
                {
                    playerTeam = team;
                    break;
                }
            }

            if (playerTeam == null)
                return;

            // If the player is already in a team, check if he is in the right team
            RelationshipManager.PlayerTeam currentTeam = RelationshipManager.ServerInstance.FindPlayersTeam(player.userID);
            if (currentTeam != null)
            {
                List<ulong> teamMemberIDs = currentTeam.members.ToList();
                bool correctTeam = true;
                
                foreach (ulong memberId in teamMemberIDs)
                {
                    if (!playerTeam.MemberSteamIDs.Contains(memberId))
                    {
                        correctTeam = false;
                        break;
                    }
                }

                if (correctTeam && teamMemberIDs.Count > 0)
                {
                    AddMissingTeamMembers(currentTeam, playerTeam);
                    return;
                }
                else
                {
                    RelationshipManager.ServerInstance.playerToTeam.Remove(player.userID);
                    currentTeam.members.Remove(player.userID);
                }
            }

            CreateOrJoinConfiguredTeam(player, playerTeam);
        }

        private void CreateOrJoinConfiguredTeam(BasePlayer player, TeamConfig teamConfig)
        {
            foreach (ulong memberId in teamConfig.MemberSteamIDs)
            {
                if (memberId == player.userID)
                    continue;

                BasePlayer teamMember = BasePlayer.FindByID(memberId);
                if (teamMember != null && teamMember.IsConnected)
                {
                    RelationshipManager.PlayerTeam existingTeam = RelationshipManager.ServerInstance.FindPlayersTeam(teamMember.userID);
                    if (existingTeam != null)
                    {
                        existingTeam.AddPlayer(player);
                        player.SendNetworkUpdate();
                        return;
                    }
                }
            }

            RelationshipManager.PlayerTeam newTeam = RelationshipManager.ServerInstance.CreateTeam();
            newTeam.teamName = teamConfig.TeamName;
            newTeam.AddPlayer(player);
            player.SendNetworkUpdate();

            foreach (ulong memberId in teamConfig.MemberSteamIDs)
            {
                if (memberId == player.userID)
                    continue;

                BasePlayer teamMember = BasePlayer.FindByID(memberId);
                if (teamMember != null && teamMember.IsConnected)
                {
                    newTeam.AddPlayer(teamMember);
                    teamMember.SendNetworkUpdate();
                }
            }
        }

        private void AddMissingTeamMembers(RelationshipManager.PlayerTeam currentTeam, TeamConfig teamConfig)
        {
            foreach (ulong memberId in teamConfig.MemberSteamIDs)
            {
                if (!currentTeam.members.Contains(memberId))
                {
                    BasePlayer teamMember = BasePlayer.FindByID(memberId);
                    if (teamMember != null && teamMember.IsConnected)
                    {
                        currentTeam.AddPlayer(teamMember);
                        teamMember.SendNetworkUpdate();
                    }
                }
            }
        }

        private void CheckAllTeams()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                AssignPlayerToTeam(player);
            }
        }

        #endregion

        #region Commands

        [ChatCommand("teamreload")]
        private void CmdReloadTeams(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                SendReply(player, "You cannot use this command.");
                return;
            }

            LoadConfig();
            CheckAllTeams();
            SendReply(player, "Team configuration reloaded and teams updated.");
        }

        [ChatCommand("teamstatus")]
        private void CmdTeamStatus(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                SendReply(player, "You cannot use this command.");
                return;
            }

            string response = "Current Team Status:\n";
            
            foreach (var team in configData.Teams)
            {
                response += $"\n{team.TeamName}:\n";
                foreach (var steamId in team.MemberSteamIDs)
                {
                    BasePlayer teamPlayer = BasePlayer.FindByID(steamId);
                    string playerName = teamPlayer != null ? teamPlayer.displayName : "Offline";
                    string status = teamPlayer != null && teamPlayer.IsConnected ? "Online" : "Offline";
                    response += $"  - {steamId} ({playerName}): {status}\n";
                }
            }
            
            SendReply(player, response);
        }

        [ChatCommand("teamadd")]
        private void CmdAddToTeam(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                SendReply(player, "You cannot use this command.");
                return;
            }

            if (args.Length < 2)
            {
                SendReply(player, "Usage: /teamadd <team name> <steam64ID>");
                return;
            }

            string teamName = args[0];
            ulong steamID;
            if (!ulong.TryParse(args[1], out steamID))
            {
                SendReply(player, "Invalid Steam64 ID.");
                return;
            }

            TeamConfig targetTeam = null;
            foreach (var team in configData.Teams)
            {
                if (team.TeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase))
                {
                    targetTeam = team;
                    break;
                }
            }

            if (targetTeam == null)
            {
                targetTeam = new TeamConfig
                {
                    TeamName = teamName,
                    MemberSteamIDs = new List<ulong>()
                };
                configData.Teams.Add(targetTeam);
            }

            if (targetTeam.MemberSteamIDs.Contains(steamID))
            {
                SendReply(player, $"Player is already in team {teamName}.");
                return;
            }

            targetTeam.MemberSteamIDs.Add(steamID);
            SaveConfig();

            BasePlayer targetPlayer = BasePlayer.FindByID(steamID);
            if (targetPlayer != null && targetPlayer.IsConnected)
            {
                AssignPlayerToTeam(targetPlayer);
            }

            SendReply(player, $"Player {steamID} has been successfully added to team {teamName}.");
        }

        [ChatCommand("teamremove")]
        private void CmdRemoveFromTeam(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                SendReply(player, "You cannot use this command.");
                return;
            }

            if (args.Length < 2)
            {
                SendReply(player, "Usage: /teamremove <team name> <steam64ID>");
                return;
            }

            string teamName = args[0];
            ulong steamID;
            if (!ulong.TryParse(args[1], out steamID))
            {
                SendReply(player, "Invalid Steam64 ID.");
                return;
            }

            TeamConfig targetTeam = null;
            foreach (var team in configData.Teams)
            {
                if (team.TeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase))
                {
                    targetTeam = team;
                    break;
                }
            }

            if (targetTeam == null)
            {
                SendReply(player, $"Team not found: {teamName}");
                return;
            }

            if (!targetTeam.MemberSteamIDs.Contains(steamID))
            {
                SendReply(player, $"Player {steamID} not found in team: {teamName}");
                return;
            }

            targetTeam.MemberSteamIDs.Remove(steamID);
            SaveConfig();

            BasePlayer targetPlayer = BasePlayer.FindByID(steamID);
            if (targetPlayer != null && targetPlayer.IsConnected)
            {
                RelationshipManager.PlayerTeam currentTeam = RelationshipManager.ServerInstance.FindPlayersTeam(targetPlayer.userID);
                if (currentTeam != null)
                {
                    RelationshipManager.ServerInstance.playerToTeam.Remove(targetPlayer.userID);
                    currentTeam.members.Remove(targetPlayer.userID);
                    targetPlayer.SendNetworkUpdate();
                }
            }

            SendReply(player, $"Player {steamID} successfully removed from team {teamName}.");
        }

        [ChatCommand("teamcreate")]
        private void CmdCreateTeam(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                SendReply(player, "You do not have permission to use this command.");
                return;
            }

            if (args.Length < 1)
            {
                SendReply(player, "Usage: /teamcreate <team name>");
                return;
            }

            string teamName = args[0];

            // Check if team already exists
            foreach (var team in configData.Teams)
            {
                if (team.TeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase))
                {
                    SendReply(player, $"Team already exists: {teamName}");
                    return;
                }
            }

            // Create new team
            TeamConfig newTeam = new TeamConfig
            {
                TeamName = teamName,
                MemberSteamIDs = new List<ulong>()
            };
            configData.Teams.Add(newTeam);
            SaveConfig();

            SendReply(player, $"New team created: {teamName}");
        }

        [ChatCommand("teamdelete")]
        private void CmdDeleteTeam(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                SendReply(player, "You do not have permission to use this command.");
                return;
            }

            if (args.Length < 1)
            {
                SendReply(player, "Usage: /teamdelete <team name>");
                return;
            }

            string teamName = args[0];

            // Find team
            TeamConfig targetTeam = null;
            int teamIndex = -1;
            for (int i = 0; i < configData.Teams.Count; i++)
            {
                if (configData.Teams[i].TeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase))
                {
                    targetTeam = configData.Teams[i];
                    teamIndex = i;
                    break;
                }
            }

            // If team not found, return error
            if (targetTeam == null)
            {
                SendReply(player, $"Team not found: {teamName}");
                return;
            }

            // Remove players from team
            foreach (ulong memberId in targetTeam.MemberSteamIDs.ToList())
            {
                BasePlayer targetPlayer = BasePlayer.FindByID(memberId);
                if (targetPlayer != null && targetPlayer.IsConnected)
                {
                    RelationshipManager.PlayerTeam currentTeam = RelationshipManager.ServerInstance.FindPlayersTeam(targetPlayer.userID);
                    if (currentTeam != null)
                    {
                        RelationshipManager.ServerInstance.playerToTeam.Remove(targetPlayer.userID);
                        currentTeam.members.Remove(targetPlayer.userID);
                        targetPlayer.SendNetworkUpdate();
                    }
                }
            }

            // Delete team
            configData.Teams.RemoveAt(teamIndex);
            SaveConfig();

            SendReply(player, $"Team deleted: {teamName}");
        }

        [ChatCommand("teamlist")]
        private void CmdListTeams(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                SendReply(player, "You do not have permission to use this command.");
                return;
            }

            string response = "Available Teams:\n";
            
            foreach (var team in configData.Teams)
            {
                response += $"\n{team.TeamName} - {team.MemberSteamIDs.Count} members";
            }
            
            SendReply(player, response);
        }

        [ChatCommand("teaminfo")]
        private void CmdTeamInfo(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                SendReply(player, "You do not have permission to use this command.");
                return;
            }

            if (args.Length < 1)
            {
                SendReply(player, "Usage: /teaminfo <team name>");
                return;
            }

            string teamName = args[0];

            // Find team
            TeamConfig targetTeam = null;
            foreach (var team in configData.Teams)
            {
                if (team.TeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase))
                {
                    targetTeam = team;
                    break;
                }
            }

            // If team not found, return error
            if (targetTeam == null)
            {
                SendReply(player, $"Team not found: {teamName}");
                return;
            }

            string response = $"Team Information: {targetTeam.TeamName}\n";
            response += $"Member Count: {targetTeam.MemberSteamIDs.Count}\n\n";
            
            foreach (var steamId in targetTeam.MemberSteamIDs)
            {
                BasePlayer teamPlayer = BasePlayer.FindByID(steamId);
                string playerName = teamPlayer != null ? teamPlayer.displayName : "Offline";
                string status = teamPlayer != null && teamPlayer.IsConnected ? "Online" : "Offline";
                response += $"  - {steamId} ({playerName}): {status}\n";
            }
            
            SendReply(player, response);
        }

        [ChatCommand("myteam")]
        private void CmdMyTeam(BasePlayer player, string command, string[] args)
        {
            // Check which team the player belongs to
            TeamConfig playerTeam = null;
            foreach (var team in configData.Teams)
            {
                if (team.MemberSteamIDs.Contains(player.userID))
                {
                    playerTeam = team;
                    break;
                }
            }

            // If player doesn't belong to any team, inform them
            if (playerTeam == null)
            {
                SendReply(player, "You do not belong to any team.");
                return;
            }

            string response = $"Your Team: {playerTeam.TeamName}\n";
            response += $"Member Count: {playerTeam.MemberSteamIDs.Count}\n\n";
            
            foreach (var steamId in playerTeam.MemberSteamIDs)
            {
                BasePlayer teamPlayer = BasePlayer.FindByID(steamId);
                string playerName = teamPlayer != null ? teamPlayer.displayName : "Offline";
                string status = teamPlayer != null && teamPlayer.IsConnected ? "Online" : "Offline";
                response += $"  - {playerName}: {status}\n";
            }
            
            SendReply(player, response);
        }

        #endregion
    }
}