﻿using Cysharp.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace MultiplayerARPG.MMO
{
#nullable disable
    [Authorize]
    [ApiController]
    public partial class ApiController : ControllerBase
    {
        public static bool IsConfigRead { get; private set; } = false;
        public static GuildRoleData[] GuildMemberRoles { get; private set; } = new GuildRoleData[] {
            new GuildRoleData() { roleName = "Master", canInvite = true, canKick = true, canUseStorage = true },
            new GuildRoleData() { roleName = "Member 1", canInvite = false, canKick = false, canUseStorage = false },
            new GuildRoleData() { roleName = "Member 2", canInvite = false, canKick = false, canUseStorage = false },
            new GuildRoleData() { roleName = "Member 3", canInvite = false, canKick = false, canUseStorage = false },
            new GuildRoleData() { roleName = "Member 4", canInvite = false, canKick = false, canUseStorage = false },
            new GuildRoleData() { roleName = "Member 5", canInvite = false, canKick = false, canUseStorage = false },
        };
        public static int[] GuildExpTree { get; private set; } = new int[0];

        private readonly ILogger<ApiController> _logger;
        private readonly IConfiguration _config;
        public bool DisableCacheReading { get; private set; }
        public IDatabaseCache DatabaseCache { get; private set; }

        public IDatabase Database { get; private set; }

        public ApiController(
            ILogger<ApiController> logger,
            IConfiguration config,
            IDatabaseCache databaseCache,
            IDatabase database)
        {
            _logger = logger;
            _config = config;
            DatabaseCache = databaseCache;
            Database = database;

            if (IsConfigRead)
                return;

            IsConfigRead = true;
            // Social System Setting
            bool configFileFound = false;
            string configFolder = "./Config";
            string configFilePath = configFolder + "/socialSystemSetting.json";
            SocialSystemSetting socialSystemSetting = new SocialSystemSetting()
            {
                GuildMemberRoles = GuildMemberRoles,
                GuildExpTree = GuildExpTree,
            };

            _logger.LogInformation("Reading social system setting config file from " + configFilePath);
            if (System.IO.File.Exists(configFilePath))
            {
                _logger.LogInformation("Found social system setting config file");
                string dataAsJson = System.IO.File.ReadAllText(configFilePath);
                SocialSystemSetting replacingConfig = JsonConvert.DeserializeObject<SocialSystemSetting>(dataAsJson);
                if (replacingConfig.GuildMemberRoles != null)
                    socialSystemSetting.GuildMemberRoles = replacingConfig.GuildMemberRoles;
                if (replacingConfig.GuildExpTree != null)
                    socialSystemSetting.GuildExpTree = replacingConfig.GuildExpTree;
                configFileFound = true;
            }

            GuildMemberRoles = socialSystemSetting.GuildMemberRoles;
            GuildExpTree = socialSystemSetting.GuildExpTree;

            if (!configFileFound)
            {
                // Write config file
                _logger.LogInformation("Not found social system setting config file, creating a new one");
                if (!Directory.Exists(configFolder))
                    Directory.CreateDirectory(configFolder);
                System.IO.File.WriteAllText(configFilePath, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
        }

        [HttpPost($"/api/{DatabaseApiPath.ValidateUserLogin}")]
        public async UniTask<IActionResult> ValidateUserLogin(ValidateUserLoginReq request)
        {
            return Ok(new ValidateUserLoginResp()
            {
                UserId = await Database.ValidateUserLogin(request.Username, request.Password),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.ValidateAccessToken}")]
        public async UniTask<IActionResult> ValidateAccessToken(ValidateAccessTokenReq request)
        {
            return Ok(new ValidateAccessTokenResp()
            {
                IsPass = await ValidateAccessToken(request.UserId, request.AccessToken),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.GetUserLevel}")]
        public async UniTask<IActionResult> GetUserLevel(GetUserLevelReq request)
        {
            return Ok(new GetUserLevelResp()
            {
                UserLevel = await Database.GetUserLevel(request.UserId),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.GetGold}")]
        public async UniTask<IActionResult> GetGold(GetGoldReq request)
        {
            return Ok(new GoldResp()
            {
                Gold = await ReadGold(request.UserId)
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.ChangeGold}")]
        public async UniTask<IActionResult> ChangeGold(ChangeGoldReq request)
        {
            int gold = await ReadGold(request.UserId);
            gold += request.ChangeAmount;
            // Cache the data, it will be used later
            await DatabaseCache.SetUserGold(request.UserId, gold);
            // Update data to database
            Database.UpdateGold(request.UserId, gold);
            return Ok(new GoldResp()
            {
                Gold = gold
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.GetCash}")]
        public async UniTask<IActionResult> GetCash(GetCashReq request)
        {
            return Ok(new CashResp()
            {
                Cash = await ReadCash(request.UserId)
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.ChangeCash}")]
        public async UniTask<IActionResult> ChangeCash(ChangeCashReq request)
        {
            int cash = await ReadCash(request.UserId);
            cash += request.ChangeAmount;
            // Cache the data, it will be used later
            await DatabaseCache.SetUserCash(request.UserId, cash);
            // Update data to database
            Database.UpdateCash(request.UserId, cash);
            return Ok(new CashResp()
            {
                Cash = cash
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateAccessToken}")]
        public async UniTask<IActionResult> UpdateAccessToken(UpdateAccessTokenReq request)
        {
            // Store access token to the dictionary, it will be used to validate later
            await DatabaseCache.SetUserAccessToken(request.UserId, request.AccessToken);
            // Update data to database
            Database.UpdateAccessToken(request.UserId, request.AccessToken);
            return Ok();
        }

        [HttpPost($"/api/{DatabaseApiPath.CreateUserLogin}")]
        public async UniTask<IActionResult> CreateUserLogin(CreateUserLoginReq request)
        {
            // Cache username, it will be used to validate later
            await DatabaseCache.AddUsername(request.Username);
            // Insert new user login to database
            Database.CreateUserLogin(request.Username, request.Password, request.Email);
            return Ok();
        }

        [HttpPost($"/api/{DatabaseApiPath.FindUsername}")]
        public async UniTask<IActionResult> FindUsername(FindUsernameReq request)
        {
            return Ok(new FindUsernameResp()
            {
                FoundAmount = await FindUsername(request.Username),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.CreateCharacter}")]
        public async UniTask<IActionResult> CreateCharacter(CreateCharacterReq request)
        {
            PlayerCharacterData character = request.CharacterData;
            // Insert new character to database
            Database.CreateCharacter(request.UserId, character);
            return Ok(new CharacterResp()
            {
                CharacterData = character
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.ReadCharacter}")]
        public async UniTask<IActionResult> ReadCharacter(ReadCharacterReq request)
        {
            return Ok(new CharacterResp()
            {
                CharacterData = await ReadCharacterWithUserIdValidation(request.CharacterId, request.UserId),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.ReadCharacters}")]
        public async UniTask<IActionResult> ReadCharacters(ReadCharactersReq request)
        {
            List<PlayerCharacterData> characters = await Database.ReadCharacters(request.UserId);
            // Read and cache character (or load from cache)
            long lastUpdate;
            for (int i = 0; i < characters.Count; ++i)
            {
                lastUpdate = characters[i].LastUpdate;
                characters[i] = await ReadCharacter(characters[i].Id);
                characters[i].LastUpdate = lastUpdate;
            }
            return Ok(new CharactersResp()
            {
                List = characters
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateCharacter}")]
        public async UniTask<IActionResult> UpdateCharacter(UpdateCharacterReq request)
        {
            PlayerCharacterData character = request.CharacterData;
            // Update data to database
            Database.UpdateCharacter(character);
            // Cache the data, it will be used later
            await DatabaseCache.SetPlayerCharacter(character);
            return Ok(new CharacterResp()
            {
                CharacterData = character
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.DeleteCharacter}")]
        public async UniTask<IActionResult> DeleteCharacter(DeleteCharacterReq request)
        {
            // Remove data from cache
            var playerCharacter = await ReadCharacter(request.CharacterId);
            if (playerCharacter != null)
            {
                await DatabaseCache.RemoveCharacterName(playerCharacter.CharacterName);
                await DatabaseCache.RemovePlayerCharacter(playerCharacter.Id);
                await DatabaseCache.RemoveSocialCharacter(playerCharacter.Id);
            }
            // Delete data from database
            Database.DeleteCharacter(request.UserId, request.CharacterId);
            return Ok();
        }

        [HttpPost($"/api/{DatabaseApiPath.FindCharacterName}")]
        public async UniTask<IActionResult> FindCharacterName(FindCharacterNameReq request)
        {
            return Ok(new FindCharacterNameResp()
            {
                FoundAmount = await FindCharacterName(request.CharacterName),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.FindCharacters}")]
        public async UniTask<IActionResult> FindCharacters(FindCharacterNameReq request)
        {
            return Ok(new SocialCharactersResp()
            {
                List = await Database.FindCharacters(request.FinderId, request.CharacterName, request.Skip, request.Limit)
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.CreateFriend}")]
        public async UniTask<IActionResult> CreateFriend(CreateFriendReq request)
        {
            Database.CreateFriend(request.Character1Id, request.Character2Id, request.State);
            return Ok();
        }

        [HttpPost($"/api/{DatabaseApiPath.DeleteFriend}")]
        public async UniTask<IActionResult> DeleteFriend(DeleteFriendReq request)
        {
            Database.DeleteFriend(request.Character1Id, request.Character2Id);
            return Ok();
        }

        [HttpPost($"/api/{DatabaseApiPath.ReadFriends}")]
        public async UniTask<IActionResult> ReadFriends(ReadFriendsReq request)
        {
            return Ok(new SocialCharactersResp()
            {
                List = await Database.ReadFriends(request.CharacterId, request.ReadById2, request.State, request.Skip, request.Limit),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.CreateBuilding}")]
        public async UniTask<IActionResult> CreateBuilding(CreateBuildingReq request)
        {
            BuildingSaveData building = request.BuildingData;
            // Insert data to database
            Database.CreateBuilding(request.ChannelId, request.MapName, building);
            // Cache building data
            await DatabaseCache.SetBuilding(request.ChannelId, request.MapName, building);
            return Ok(new BuildingResp()
            {
                BuildingData = request.BuildingData
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateBuilding}")]
        public async UniTask<IActionResult> UpdateBuilding(UpdateBuildingReq request)
        {
            BuildingSaveData building = request.BuildingData;
            // Update data to database
            Database.UpdateBuilding(request.ChannelId, request.MapName, building);
            // Cache building data
            await DatabaseCache.SetBuilding(request.ChannelId, request.MapName, building);
            return Ok(new BuildingResp()
            {
                BuildingData = request.BuildingData
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.DeleteBuilding}")]
        public async UniTask<IActionResult> DeleteBuilding(DeleteBuildingReq request)
        {
            // Remove data from cache
            await DatabaseCache.RemoveBuilding(request.ChannelId, request.MapName, request.BuildingId);
            // Remove data from database
            Database.DeleteBuilding(request.ChannelId, request.MapName, request.BuildingId);
            return Ok();
        }

        [HttpPost($"/api/{DatabaseApiPath.ReadBuildings}")]
        public async UniTask<IActionResult> ReadBuildings(ReadBuildingsReq request)
        {
            return Ok(new BuildingsResp()
            {
                List = await ReadBuildings(request.ChannelId, request.MapName),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.CreateParty}")]
        public async UniTask<IActionResult> CreateParty(CreatePartyReq request)
        {
            // Insert to database
            int partyId = await Database.CreateParty(request.ShareExp, request.ShareItem, request.LeaderCharacterId);
            PartyData party = new PartyData(partyId, request.ShareExp, request.ShareItem, request.LeaderCharacterId);
            // Cache the data, it will be used later
            await DatabaseCache.SetParty(party);
            return Ok(new PartyResp()
            {
                PartyData = party
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateParty}")]
        public async UniTask<IActionResult> UpdateParty(UpdatePartyReq request)
        {
            PartyData party = await ReadParty(request.PartyId);
            if (party == null)
            {
                return StatusCode(404);
            }
            party.Setting(request.ShareExp, request.ShareItem);
            // Update to cache
            await DatabaseCache.SetParty(party);
            // Update to database
            Database.UpdateParty(request.PartyId, request.ShareExp, request.ShareItem);
            return Ok(new PartyResp()
            {
                PartyData = party
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdatePartyLeader}")]
        public async UniTask<IActionResult> UpdatePartyLeader(UpdatePartyLeaderReq request)
        {
            PartyData party = await ReadParty(request.PartyId);
            if (party == null)
            {
                return StatusCode(404);
            }
            party.SetLeader(request.LeaderCharacterId);
            // Update to cache
            await DatabaseCache.SetParty(party);
            // Update to database
            Database.UpdatePartyLeader(request.PartyId, request.LeaderCharacterId);
            return Ok(new PartyResp()
            {
                PartyData = party
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.DeleteParty}")]
        public async UniTask<IActionResult> DeleteParty(DeletePartyReq request)
        {
            // Remove data from cache
            await DatabaseCache.RemoveParty(request.PartyId);
            // Remove data from database
            Database.DeleteParty(request.PartyId);
            return Ok();
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateCharacterParty}")]
        public async UniTask<IActionResult> UpdateCharacterParty(UpdateCharacterPartyReq request)
        {
            PartyData party = await ReadParty(request.PartyId);
            if (party == null)
            {
                return StatusCode(404);
            }
            SocialCharacterData character = request.SocialCharacterData;
            party.AddMember(character);
            // Update to cache
            await DatabaseCache.SetParty(party);
            await DatabaseCache.SetPlayerCharacterPartyId(character.id, party.id);
            await DatabaseCache.SetSocialCharacterPartyId(character.id, party.id);
            // Update to database
            Database.UpdateCharacterParty(character.id, request.PartyId);
            return Ok(new PartyResp()
            {
                PartyData = party
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.ClearCharacterParty}")]
        public async UniTask<IActionResult> ClearCharacterParty(ClearCharacterPartyReq request)
        {
            PlayerCharacterData character = await ReadCharacter(request.CharacterId);
            if (character == null)
            {
                return Ok();
            }
            PartyData party = await ReadParty(character.PartyId);
            if (party == null)
            {
                return Ok();
            }
            party.RemoveMember(request.CharacterId);
            // Update to cache
            await DatabaseCache.SetParty(party);
            await DatabaseCache.SetPlayerCharacterPartyId(request.CharacterId, 0);
            await DatabaseCache.SetSocialCharacterPartyId(request.CharacterId, 0);
            // Update to database
            Database.UpdateCharacterParty(request.CharacterId, 0);
            return Ok();
        }

        [HttpPost($"/api/{DatabaseApiPath.ReadParty}")]
        public async UniTask<IActionResult> ReadParty(ReadPartyReq request)
        {
            return Ok(new PartyResp()
            {
                PartyData = await ReadParty(request.PartyId)
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.CreateGuild}")]
        public async UniTask<IActionResult> CreateGuild(CreateGuildReq request)
        {
            // Insert to database
            int guildId = await Database.CreateGuild(request.GuildName, request.LeaderCharacterId);
            GuildData guild = new GuildData(guildId, request.GuildName, request.LeaderCharacterId, GuildMemberRoles);
            // Cache the data, it will be used later
            await DatabaseCache.SetGuild(guild);
            return Ok(new GuildResp()
            {
                GuildData = guild
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateGuildLeader}")]
        public async UniTask<IActionResult> UpdateGuildLeader(UpdateGuildLeaderReq request)
        {
            GuildData guild = await ReadGuild(request.GuildId);
            if (guild == null)
            {
                return StatusCode(404);
            }
            guild.SetLeader(request.LeaderCharacterId);
            // Update to cache
            await DatabaseCache.SetGuild(guild);
            // Update to database
            Database.UpdateGuildLeader(request.GuildId, request.LeaderCharacterId);
            return Ok(new GuildResp()
            {
                GuildData = guild
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateGuildMessage}")]
        public async UniTask<IActionResult> UpdateGuildMessage(UpdateGuildMessageReq request)
        {
            GuildData guild = await ReadGuild(request.GuildId);
            if (guild == null)
            {
                return StatusCode(404);
            }
            guild.guildMessage = request.GuildMessage;
            // Update to cache
            await DatabaseCache.SetGuild(guild);
            // Update to database
            Database.UpdateGuildMessage(request.GuildId, request.GuildMessage);
            return Ok(new GuildResp()
            {
                GuildData = guild
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateGuildMessage2}")]
        public async UniTask<IActionResult> UpdateGuildMessage2(UpdateGuildMessageReq request)
        {
            GuildData guild = await ReadGuild(request.GuildId);
            if (guild == null)
            {
                return StatusCode(404);
            }
            guild.guildMessage2 = request.GuildMessage;
            // Update to cache
            await DatabaseCache.SetGuild(guild);
            // Update to database
            Database.UpdateGuildMessage2(request.GuildId, request.GuildMessage);
            return Ok(new GuildResp()
            {
                GuildData = guild
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateGuildScore}")]
        public async UniTask<IActionResult> UpdateGuildScore(UpdateGuildScoreReq request)
        {
            GuildData guild = await ReadGuild(request.GuildId);
            if (guild == null)
            {
                return StatusCode(404);
            }
            guild.score = request.Score;
            // Update to cache
            await DatabaseCache.SetGuild(guild);
            // Update to database
            Database.UpdateGuildScore(request.GuildId, request.Score);
            return Ok(new GuildResp()
            {
                GuildData = guild
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateGuildOptions}")]
        public async UniTask<IActionResult> UpdateGuildOptions(UpdateGuildOptionsReq request)
        {
            GuildData guild = await ReadGuild(request.GuildId);
            if (guild == null)
            {
                return StatusCode(404);
            }
            guild.options = request.Options;
            // Update to cache
            await DatabaseCache.SetGuild(guild);
            // Update to database
            Database.UpdateGuildOptions(request.GuildId, request.Options);
            return Ok(new GuildResp()
            {
                GuildData = guild
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateGuildAutoAcceptRequests}")]
        public async UniTask<IActionResult> UpdateGuildAutoAcceptRequests(UpdateGuildAutoAcceptRequestsReq request)
        {
            GuildData guild = await ReadGuild(request.GuildId);
            if (guild == null)
            {
                return StatusCode(404);
            }
            guild.autoAcceptRequests = request.AutoAcceptRequests;
            // Update to cache
            await DatabaseCache.SetGuild(guild);
            // Update to database
            Database.UpdateGuildAutoAcceptRequests(request.GuildId, request.AutoAcceptRequests);
            return Ok(new GuildResp()
            {
                GuildData = guild
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateGuildRank}")]
        public async UniTask<IActionResult> UpdateGuildRank(UpdateGuildRankReq request)
        {
            GuildData guild = await ReadGuild(request.GuildId);
            if (guild == null)
            {
                return StatusCode(404);
            }
            guild.score = request.Rank;
            // Update to cache
            await DatabaseCache.SetGuild(guild);
            // Update to database
            Database.UpdateGuildRank(request.GuildId, request.Rank);
            return Ok(new GuildResp()
            {
                GuildData = guild
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateGuildRole}")]
        public async UniTask<IActionResult> UpdateGuildRole(UpdateGuildRoleReq request)
        {
            GuildData guild = await ReadGuild(request.GuildId);
            if (guild == null)
            {
                return StatusCode(404);
            }
            guild.SetRole(request.GuildRole, request.GuildRoleData);
            // Update to cache
            await DatabaseCache.SetGuild(guild);
            // Update to database
            Database.UpdateGuildRole(request.GuildId, request.GuildRole, request.GuildRoleData);
            return Ok(new GuildResp()
            {
                GuildData = guild
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateGuildMemberRole}")]
        public async UniTask<IActionResult> UpdateGuildMemberRole(UpdateGuildMemberRoleReq request)
        {
            GuildData guild = await ReadGuild(request.GuildId);
            if (guild == null)
            {
                return StatusCode(404);
            }
            guild.SetMemberRole(request.MemberCharacterId, request.GuildRole);
            // Update to cache
            await DatabaseCache.SetGuild(guild);
            // Update to database
            Database.UpdateGuildMemberRole(request.MemberCharacterId, request.GuildRole);
            return Ok(new GuildResp()
            {
                GuildData = guild
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.DeleteGuild}")]
        public async UniTask<IActionResult> DeleteGuild(DeleteGuildReq request)
        {
            // Remove data from cache
            var guild = await ReadGuild(request.GuildId);
            if (guild != null)
            {
                await DatabaseCache.RemoveGuildName(guild.guildName);
                await DatabaseCache.RemoveGuild(guild.id);
            }
            // Remove data from database
            Database.DeleteGuild(request.GuildId);
            return Ok();
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateCharacterGuild}")]
        public async UniTask<IActionResult> UpdateCharacterGuild(UpdateCharacterGuildReq request)
        {
            GuildData guild = await ReadGuild(request.GuildId);
            if (guild == null)
            {
                return StatusCode(404);
            }
            SocialCharacterData character = request.SocialCharacterData;
            guild.AddMember(character, request.GuildRole);
            // Update to cache
            await DatabaseCache.SetGuild(guild);
            await DatabaseCache.SetPlayerCharacterGuildIdAndRole(character.id, guild.id, request.GuildRole);
            await DatabaseCache.SetSocialCharacterGuildIdAndRole(character.id, guild.id, request.GuildRole);
            // Update to database
            Database.UpdateCharacterGuild(character.id, request.GuildId, request.GuildRole);
            return Ok(new GuildResp()
            {
                GuildData = guild
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.ClearCharacterGuild}")]
        public async UniTask<IActionResult> ClearCharacterGuild(ClearCharacterGuildReq request)
        {
            PlayerCharacterData character = await ReadCharacter(request.CharacterId);
            if (character == null)
            {
                return Ok();
            }
            GuildData guild = await ReadGuild(character.GuildId);
            if (guild == null)
            {
                return Ok();
            }
            guild.RemoveMember(request.CharacterId);
            // Update to cache
            await DatabaseCache.SetGuild(guild);
            await DatabaseCache.SetPlayerCharacterGuildIdAndRole(request.CharacterId, 0, 0);
            await DatabaseCache.SetSocialCharacterGuildIdAndRole(request.CharacterId, 0, 0);
            // Update to database
            Database.UpdateCharacterGuild(request.CharacterId, 0, 0);
            return Ok();
        }

        [HttpPost($"/api/{DatabaseApiPath.FindGuildName}")]
        public async UniTask<IActionResult> FindGuildName(FindGuildNameReq request)
        {
            return Ok(new FindGuildNameResp()
            {
                FoundAmount = await FindGuildName(request.GuildName),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.ReadGuild}")]
        public async UniTask<IActionResult> ReadGuild(ReadGuildReq request)
        {
            return Ok(new GuildResp()
            {
                GuildData = await ReadGuild(request.GuildId)
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.IncreaseGuildExp}")]
        public async UniTask<IActionResult> IncreaseGuildExp(IncreaseGuildExpReq request)
        {
            // TODO: May validate guild by character
            GuildData guild = await ReadGuild(request.GuildId);
            if (guild == null)
            {
                return StatusCode(404);
            }
            guild.IncreaseGuildExp(GuildExpTree, request.Exp);
            // Update to cache
            await DatabaseCache.SetGuild(guild);
            // Update to database
            Database.UpdateGuildLevel(request.GuildId, guild.level, guild.exp, guild.skillPoint);
            return Ok(new GuildResp()
            {
                GuildData = await ReadGuild(request.GuildId)
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.AddGuildSkill}")]
        public async UniTask<IActionResult> AddGuildSkill(AddGuildSkillReq request)
        {
            // TODO: May validate guild by character
            GuildData guild = await ReadGuild(request.GuildId);
            if (guild == null)
            {
                return StatusCode(404);
            }
            guild.AddSkillLevel(request.SkillId);
            // Update to cache
            await DatabaseCache.SetGuild(guild);
            // Update to database
            Database.UpdateGuildSkillLevel(request.GuildId, request.SkillId, guild.GetSkillLevel(request.SkillId), guild.skillPoint);
            return Ok(new GuildResp()
            {
                GuildData = await ReadGuild(request.GuildId)
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.GetGuildGold}")]
        public async UniTask<IActionResult> GetGuildGold(GetGuildGoldReq request)
        {
            GuildData guild = await ReadGuild(request.GuildId);
            if (guild == null)
            {
                return StatusCode(404);
            }
            return Ok(new GuildGoldResp()
            {
                GuildGold = guild.gold
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.ChangeGuildGold}")]
        public async UniTask<IActionResult> ChangeGuildGold(ChangeGuildGoldReq request)
        {
            GuildData guild = await ReadGuild(request.GuildId);
            if (guild == null)
            {
                return StatusCode(404);
            }
            guild.gold += request.ChangeAmount;
            // Update to cache
            await DatabaseCache.SetGuild(guild);
            // Update to database
            Database.UpdateGuildGold(request.GuildId, guild.gold);
            return Ok(new GuildGoldResp()
            {
                GuildGold = guild.gold
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.ReadStorageItems}")]
        public async UniTask<IActionResult> ReadStorageItems(ReadStorageItemsReq request)
        {
            StorageId storageId = new StorageId(request.StorageType, request.StorageOwnerId);
            if (request.ReadForUpdate)
            {
                long time = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                var updatedTime = await DatabaseCache.GetUpdatingStorage(storageId);
                if (updatedTime.HasValue && time - updatedTime.Value < 500)
                {
                    // Not allow to update yet
                    return StatusCode(400);
                }
                await DatabaseCache.SetUpdatingStorage(storageId, time);
            }
            return Ok(new ReadStorageItemsResp()
            {
                StorageCharacterItems = await ReadStorageItems(storageId),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateStorageItems}")]
        public async UniTask<IActionResult> UpdateStorageItems(UpdateStorageItemsReq request)
        {
            StorageId storageId = new StorageId(request.StorageType, request.StorageOwnerId);
            long time = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var updatedTime = await DatabaseCache.GetUpdatingStorage(storageId);
            if (updatedTime.HasValue && time - updatedTime.Value >= 500)
            {
                // Timeout
                return StatusCode(400);
            }
            if (request.CharacterData != null)
            {
                PlayerCharacterData character = request.CharacterData;
                // Cache the data, it will be used later
                await DatabaseCache.SetPlayerCharacter(character);
                // Update data to database
                Database.UpdateCharacter(character);
            }
            // Cache the data, it will be used later
            await DatabaseCache.SetStorageItems(storageId, request.StorageItems);
            await DatabaseCache.RemoveUpdatingStorage(storageId);
            // Update data to database
            Database.UpdateStorageItems(request.StorageType, request.StorageOwnerId, request.StorageItems);
            return Ok();
        }

        [HttpPost($"/api/{DatabaseApiPath.MailList}")]
        public async UniTask<IActionResult> MailList(MailListReq request)
        {
            return Ok(new MailListResp()
            {
                List = await Database.MailList(request.UserId, request.OnlyNewMails)
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateReadMailState}")]
        public async UniTask<IActionResult> UpdateReadMailState(UpdateReadMailStateReq request)
        {
            long updated = await Database.UpdateReadMailState(request.MailId, request.UserId);
            if (updated <= 0)
            {
                return StatusCode(400, new SendMailResp()
                {
                    Error = UITextKeys.UI_ERROR_MAIL_READ_NOT_ALLOWED
                });
            }
            return Ok(new UpdateReadMailStateResp()
            {
                Mail = await Database.GetMail(request.MailId, request.UserId)
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateClaimMailItemsState}")]
        public async UniTask<IActionResult> UpdateClaimMailItemsState(UpdateClaimMailItemsStateReq request)
        {
            long updated = await Database.UpdateClaimMailItemsState(request.MailId, request.UserId);
            if (updated <= 0)
            {
                return StatusCode(400, new SendMailResp()
                {
                    Error = UITextKeys.UI_ERROR_MAIL_CLAIM_NOT_ALLOWED
                });
            }
            return Ok(new UpdateClaimMailItemsStateResp()
            {
                Mail = await Database.GetMail(request.MailId, request.UserId)
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateDeleteMailState}")]
        public async UniTask<IActionResult> UpdateDeleteMailState(UpdateDeleteMailStateReq request)
        {
            long updated = await Database.UpdateDeleteMailState(request.MailId, request.UserId);
            if (updated <= 0)
            {
                return StatusCode(400, new SendMailResp()
                {
                    Error = UITextKeys.UI_ERROR_MAIL_DELETE_NOT_ALLOWED
                });
            }
            return Ok(new UpdateDeleteMailStateResp());
        }

        [HttpPost($"/api/{DatabaseApiPath.SendMail}")]
        public async UniTask<IActionResult> SendMail(SendMailReq request)
        {
            Mail mail = request.Mail;
            if (string.IsNullOrEmpty(mail.ReceiverId))
            {
                return StatusCode(400, new SendMailResp()
                {
                    Error = UITextKeys.UI_ERROR_MAIL_SEND_NO_RECEIVER
                });
            }
            long created = await Database.CreateMail(mail);
            if (created <= 0)
            {
                return StatusCode(500, new SendMailResp()
                {
                    Error = UITextKeys.UI_ERROR_MAIL_SEND_NOT_ALLOWED
                });
            }
            return Ok(new SendMailResp());
        }

        [HttpPost($"/api/{DatabaseApiPath.GetMail}")]
        public async UniTask<IActionResult> GetMail(GetMailReq request)
        {
            return Ok(new GetMailResp()
            {
                Mail = await Database.GetMail(request.MailId, request.UserId),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.GetMailNotification}")]
        public async UniTask<IActionResult> GetMailNotification(GetMailNotificationReq request)
        {
            return Ok(new GetMailNotificationResp()
            {
                NotificationCount = await Database.GetMailNotification(request.UserId),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.GetIdByCharacterName}")]
        public async UniTask<IActionResult> GetIdByCharacterName(GetIdByCharacterNameReq request)
        {
            return Ok(new GetIdByCharacterNameResp()
            {
                Id = await Database.GetIdByCharacterName(request.CharacterName),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.GetUserIdByCharacterName}")]
        public async UniTask<IActionResult> GetUserIdByCharacterName(GetUserIdByCharacterNameReq request)
        {
            return Ok(new GetUserIdByCharacterNameResp()
            {
                UserId = await Database.GetUserIdByCharacterName(request.CharacterName),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.GetUserUnbanTime}")]
        public async UniTask<IActionResult> GetUserUnbanTime(GetUserUnbanTimeReq request)
        {
            long unbanTime = await Database.GetUserUnbanTime(request.UserId);
            return Ok(new GetUserUnbanTimeResp()
            {
                UnbanTime = unbanTime,
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.SetUserUnbanTimeByCharacterName}")]
        public async UniTask<IActionResult> SetUserUnbanTimeByCharacterName(SetUserUnbanTimeByCharacterNameReq request)
        {
            Database.SetUserUnbanTimeByCharacterName(request.CharacterName, request.UnbanTime);
            return Ok();
        }

        [HttpPost($"/api/{DatabaseApiPath.SetCharacterUnmuteTimeByName}")]
        public async UniTask<IActionResult> SetCharacterUnmuteTimeByName(SetCharacterUnmuteTimeByNameReq request)
        {
            Database.SetCharacterUnmuteTimeByName(request.CharacterName, request.UnmuteTime);
            return Ok();
        }

        [HttpPost($"/api/{DatabaseApiPath.GetSummonBuffs}")]
        public async UniTask<IActionResult> GetSummonBuffs(GetSummonBuffsReq request)
        {
            return Ok(new GetSummonBuffsResp()
            {
                SummonBuffs = await Database.GetSummonBuffs(request.CharacterId),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.SetSummonBuffs}")]
        public async UniTask<IActionResult> SetSummonBuffs(SetSummonBuffsReq request)
        {
            Database.SetSummonBuffs(request.CharacterId, request.SummonBuffs);
            return Ok();
        }

        [HttpPost($"/api/{DatabaseApiPath.FindEmail}")]
        public async UniTask<IActionResult> FindEmail(FindEmailReq request)
        {
            return Ok(new FindEmailResp()
            {
                FoundAmount = await FindEmail(request.Email),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.ValidateEmailVerification}")]
        public async UniTask<IActionResult> ValidateEmailVerification(ValidateEmailVerificationReq request)
        {
            return Ok(new ValidateEmailVerificationResp()
            {
                IsPass = await Database.ValidateEmailVerification(request.UserId),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.GetFriendRequestNotification}")]
        public async UniTask<IActionResult> GetFriendRequestNotification(GetFriendRequestNotificationReq request)
        {
            return Ok(new GetFriendRequestNotificationResp()
            {
                NotificationCount = await Database.GetFriendRequestNotification(request.CharacterId),
            });
        }

        [HttpPost($"/api/{DatabaseApiPath.UpdateUserCount}")]
        public async UniTask<IActionResult> UpdateUserCount(UpdateUserCountReq request)
        {
            Database.UpdateUserCount(request.UserCount);
            return Ok();
        }

        protected async UniTask<bool> ValidateAccessToken(string userId, string accessToken)
        {
            if (!DisableCacheReading)
            {
                // Already cached access token, so validate access token from cache
                var accessTokenResult = await DatabaseCache.GetUserAccessToken(userId);
                return accessTokenResult.HasValue && accessToken.Equals(accessTokenResult.Value);
            }
            // Doesn't cached yet, so try validate from database
            if (await Database.ValidateAccessToken(userId, accessToken))
            {
                // Pass, store access token to the dictionary
                await DatabaseCache.SetUserAccessToken(userId, accessToken);
                return true;
            }
            return false;
        }

        protected async UniTask<long> FindUsername(string username)
        {
            long foundAmount;
            if (!DisableCacheReading && await DatabaseCache.ContainsUsername(username))
            {
                // Already cached username, so validate username from cache
                foundAmount = 1;
            }
            else
            {
                // Doesn't cached yet, so try validate from database
                foundAmount = await Database.FindUsername(username);
                // Cache username, it will be used to validate later
                if (foundAmount > 0)
                    await DatabaseCache.AddUsername(username);
            }
            return foundAmount;
        }

        protected async UniTask<long> FindCharacterName(string characterName)
        {
            long foundAmount;
            if (!DisableCacheReading && await DatabaseCache.ContainsCharacterName(characterName))
            {
                // Already cached character name, so validate character name from cache
                foundAmount = 1;
            }
            else
            {
                // Doesn't cached yet, so try validate from database
                foundAmount = await Database.FindCharacterName(characterName);
                // Cache character name, it will be used to validate later
                if (foundAmount > 0)
                    await DatabaseCache.AddCharacterName(characterName);
            }
            return foundAmount;
        }

        protected async UniTask<long> FindGuildName(string guildName)
        {
            long foundAmount;
            if (!DisableCacheReading && await DatabaseCache.ContainsGuildName(guildName))
            {
                // Already cached username, so validate username from cache
                foundAmount = 1;
            }
            else
            {
                // Doesn't cached yet, so try validate from database
                foundAmount = await Database.FindGuildName(guildName);
                // Cache guild name, it will be used to validate later
                if (foundAmount > 0)
                    await DatabaseCache.AddGuildName(guildName);
            }
            return foundAmount;
        }

        protected async UniTask<long> FindEmail(string email)
        {
            long foundAmount;
            if (!DisableCacheReading && await DatabaseCache.ContainsEmail(email))
            {
                // Already cached username, so validate username from cache
                foundAmount = 1;
            }
            else
            {
                // Doesn't cached yet, so try validate from database
                foundAmount = await Database.FindEmail(email);
                // Cache username, it will be used to validate later
                if (foundAmount > 0)
                    await DatabaseCache.AddEmail(email);
            }
            return foundAmount;
        }

        protected async UniTask<List<BuildingSaveData>> ReadBuildings(string channelId, string mapName)
        {
            if (!DisableCacheReading)
            {
                // Get buildings from cache
                var buildingsResult = await DatabaseCache.GetBuildings(channelId, mapName);
                if (buildingsResult.HasValue)
                    return new List<BuildingSaveData>(buildingsResult.Value);
            }
            // Read buildings from database
            List<BuildingSaveData> buildings = await Database.ReadBuildings(channelId, mapName);
            // Store buildings to cache
            await DatabaseCache.SetBuildings(channelId, mapName, buildings);
            return buildings;
        }

        protected async UniTask<int> ReadGold(string userId)
        {
            if (!DisableCacheReading)
            {
                // Get gold from cache
                var goldResult = await DatabaseCache.GetUserGold(userId);
                if (goldResult.HasValue)
                    return goldResult.Value;
            }
            // Read gold from database
            int gold = await Database.GetGold(userId);
            // Store gold to cache
            await DatabaseCache.SetUserGold(userId, gold);
            return gold;
        }

        protected async UniTask<int> ReadCash(string userId)
        {
            if (!DisableCacheReading)
            {
                // Get cash from cache
                var cashResult = await DatabaseCache.GetUserCash(userId);
                if (cashResult.HasValue)
                    return cashResult.Value;
            }
            // Read cash from database
            int cash = await Database.GetCash(userId);
            // Store cash to cache
            await DatabaseCache.SetUserCash(userId, cash);
            return cash;
        }

        protected async UniTask<PlayerCharacterData> ReadCharacter(string id)
        {
            if (!DisableCacheReading)
            {
                // Get character from cache
                var characterResult = await DatabaseCache.GetPlayerCharacter(id);
                if (characterResult.HasValue)
                    return characterResult.Value;
            }
            // Read character from database
            PlayerCharacterData character = await Database.ReadCharacter(id);
            if (character != null)
            {
                // Store character to cache
                await DatabaseCache.SetPlayerCharacter(character);
            }
            return character;
        }

        protected async UniTask<PlayerCharacterData> ReadCharacterWithUserIdValidation(string id, string userId)
        {
            PlayerCharacterData character = await ReadCharacter(id);
            if (character != null && character.UserId != userId)
                character = null;
            return character;
        }

        protected async UniTask<SocialCharacterData> ReadSocialCharacter(string id)
        {
            if (!DisableCacheReading)
            {
                // Get character from cache
                var characterResult = await DatabaseCache.GetSocialCharacter(id);
                if (characterResult.HasValue)
                    return characterResult.Value;
            }
            // Read character from database
            SocialCharacterData character = SocialCharacterData.Create(await Database.ReadCharacter(id, false, false, false, false, false, false, false, false, false, false, false));
            // Store character to cache
            await DatabaseCache.SetSocialCharacter(character);
            return character;
        }

        protected async UniTask<PartyData> ReadParty(int id)
        {
            if (!DisableCacheReading)
            {
                // Get party from cache
                var partyResult = await DatabaseCache.GetParty(id);
                if (partyResult.HasValue)
                    return partyResult.Value;
            }
            // Read party from database
            PartyData party = await Database.ReadParty(id);
            if (party != null)
            {
                // Store party to cache
                await DatabaseCache.SetParty(party);
                await CacheSocialCharacters(party.GetMembers());
            }
            return party;
        }

        protected async UniTask<GuildData> ReadGuild(int id)
        {
            if (!DisableCacheReading)
            {
                // Get guild from cache
                var guildResult = await DatabaseCache.GetGuild(id);
                if (guildResult.HasValue)
                    return guildResult.Value;
            }
            // Read guild from database
            GuildData guild = await Database.ReadGuild(id, GuildMemberRoles);
            if (guild != null)
            {
                // Store guild to cache
                await DatabaseCache.SetGuild(guild);
                await CacheSocialCharacters(guild.GetMembers());
            }
            return guild;
        }

        protected async UniTask<List<CharacterItem>> ReadStorageItems(StorageId storageId)
        {
            if (!DisableCacheReading)
            {
                // Get storageItems from cache
                var storageItemsResult = await DatabaseCache.GetStorageItems(storageId);
                if (storageItemsResult.HasValue)
                    return new List<CharacterItem>(storageItemsResult.Value);
            }
            // Read storageItems from database
            List<CharacterItem> storageItems = await Database.ReadStorageItems(storageId.storageType, storageId.storageOwnerId);
            // Store storageItems to cache
            await DatabaseCache.SetStorageItems(storageId, storageItems);
            return storageItems;
        }

        protected async UniTask CacheSocialCharacters(SocialCharacterData[] socialCharacters)
        {
            UniTask<bool>[] tasks = new UniTask<bool>[socialCharacters.Length];
            for (int i = 0; i < socialCharacters.Length; ++i)
            {
                tasks[i] = DatabaseCache.SetSocialCharacter(socialCharacters[i]);
            }
            await UniTask.WhenAll(tasks);
        }
    }
}
