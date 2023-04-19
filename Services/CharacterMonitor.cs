﻿using System;
using System.Collections.Generic;
using System.Linq;
using CriticalCommonLib.Models;
using Dalamud.Game;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace CriticalCommonLib
{
    public class CharacterMonitor : ICharacterMonitor
    {
        private Dictionary<ulong, Character> _characters;
        
        private ulong _activeRetainer;
        private ulong _activeCharacterId;
        private ulong _activeFreeCompanyId;
        private uint? _activeClassJobId;
        private bool _isRetainerLoaded = false;
        private bool _isFreeCompanyLoaded = false;
        private Dictionary<ulong, uint> _trackedGil = new Dictionary<ulong, uint>();

        
        public CharacterMonitor()
        {
            _characters = new Dictionary<ulong, Character>();
            Service.Framework.Update += FrameworkOnOnUpdateEvent;
            RefreshActiveCharacter();
        }

        public CharacterMonitor(bool noSetup)
        {
            _characters = new();
        }

        public Character? ActiveFreeCompany =>
            _characters.ContainsKey(_activeFreeCompanyId) ? _characters[_activeFreeCompanyId] : null;
        
        public bool IsLoggedIn
        {
            get
            {
                return Service.ClientState.IsLoggedIn;
            }
        }

        public ulong LocalContentId
        {
            get
            {
                return Service.ClientState.LocalContentId;
            }
        }

        public void UpdateCharacter(Character character)
        {
            Service.Framework.RunOnFrameworkThread(() => { OnCharacterUpdated?.Invoke(character); });
        }

        public void RemoveCharacter(ulong characterId)
        {
            if (_characters.ContainsKey(characterId))
            {
                _characters.Remove(characterId);
                Service.Framework.RunOnFrameworkThread(() => { OnCharacterRemoved?.Invoke(characterId); });
            }
        }

        public unsafe void RefreshActiveCharacter()
        {
            if (Service.ClientState.IsLoggedIn && Service.ClientState.LocalPlayer != null && Service.ClientState.LocalContentId != 0)
            {
                PluginLog.Verbose("CharacterMonitor: Character has changed to " + Service.ClientState.LocalContentId);
                Character character;
                if (_characters.ContainsKey(Service.ClientState.LocalContentId))
                {
                    character = _characters[Service.ClientState.LocalContentId];
                }
                else
                {
                    character = new Character();
                    character.CharacterId = Service.ClientState.LocalContentId;
                    _characters[character.CharacterId] = character;
                }
                var infoProxy = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->UIModule->GetInfoModule()->GetInfoProxyById(InfoProxyId.FreeCompany);
                InfoProxyFreeCompany* freeCompanyInfoProxy = null;
                if (infoProxy != null)
                {
                    freeCompanyInfoProxy = (InfoProxyFreeCompany*)infoProxy;
                }
                character.UpdateFromCurrentPlayer(Service.ClientState.LocalPlayer, freeCompanyInfoProxy);
                Service.Framework.RunOnFrameworkThread(() => { OnCharacterUpdated?.Invoke(character); });
            }
            else
            {
                Service.Framework.RunOnFrameworkThread(() => { OnCharacterUpdated?.Invoke(null); });
            }
        }

        public delegate void ActiveRetainerChangedDelegate(ulong retainerId);
        public delegate void ActiveFreeCompanyChangedDelegate(ulong freeCompanyId);
        public event ActiveRetainerChangedDelegate? OnActiveRetainerChanged; 

        public event ActiveRetainerChangedDelegate? OnActiveRetainerLoaded; 
        public event ActiveFreeCompanyChangedDelegate? OnActiveFreeCompanyChanged; 
        
        public delegate void CharacterUpdatedDelegate(Character? character);
        public event CharacterUpdatedDelegate? OnCharacterUpdated;
        
        public delegate void CharacterRemovedDelegate(ulong characterId);
        public event CharacterRemovedDelegate? OnCharacterRemoved;

        public delegate void CharacterJobChangedDelegate();

        public event CharacterJobChangedDelegate? OnCharacterJobChanged;
        
        public delegate void GilUpdatedDelegate(ulong characterId, uint newGil);
        public event GilUpdatedDelegate? OnGilUpdated;

        public Dictionary<ulong, Character> Characters => _characters;

        public KeyValuePair<ulong, Character>[] GetPlayerCharacters()
        {
            return Characters.Where(c => c.Value.OwnerId == 0 && c.Value.CharacterType == CharacterType.Character && c.Key != 0 && c.Value.Name != "").ToArray();
        }

        public KeyValuePair<ulong, Character>[] GetFreeCompanies()
        {
            return Characters.Where(c => c.Value.OwnerId == 0 && c.Value.CharacterType == CharacterType.FreeCompanyChest && c.Key != 0 && c.Value.Name != "").ToArray();
        }

        public KeyValuePair<ulong, Character>[] AllCharacters()
        {
            return Characters.Where(c => c.Value.Name != "").ToArray();
        }

        public Character? GetCharacterByName(string name, ulong ownerId)
        {
            return Characters.Select(c => c.Value).FirstOrDefault(c => c.Name == name && c.OwnerId == ownerId);
        }

        public bool IsCharacter(ulong characterId)
        {
            if (Characters.ContainsKey(characterId))
            {
                return Characters[characterId].CharacterType == CharacterType.Character;
            }
            return false;
        }

        public bool IsRetainer(ulong characterId)
        {
            if (Characters.ContainsKey(characterId))
            {
                return Characters[characterId].CharacterType == CharacterType.Retainer;
            }
            return false;
        }

        public bool IsFreeCompany(ulong characterId)
        {
            if (Characters.ContainsKey(characterId))
            {
                return Characters[characterId].CharacterType == CharacterType.FreeCompanyChest;
            }
            return false;
        }

        public Character? GetCharacterById(ulong characterId)
        {
            if (Characters.ContainsKey(characterId))
            {
                return Characters[characterId];
            }
            return null;
        }
        
        public bool BelongsToActiveCharacter(ulong characterId)
        {
            if (IsFreeCompany(characterId))
            {
                var activeCharacter = ActiveCharacter;
                if (activeCharacter == null)
                {
                    return false;
                }

                return activeCharacter.FreeCompanyId == characterId;
            }
            if (characterId != 0 && Characters.ContainsKey(characterId))
            {
                return Characters[characterId].OwnerId == _activeCharacterId || Characters[characterId].CharacterId == _activeCharacterId;
            }
            return false;
        }

        public KeyValuePair<ulong, Character>[] GetRetainerCharacters(ulong retainerId)
        {
            return Characters.Where(c => c.Value.OwnerId == retainerId && c.Value.CharacterType == CharacterType.Retainer && c.Key != 0 && c.Value.Name != "").ToArray();
        }

        public KeyValuePair<ulong, Character>[] GetRetainerCharacters()
        {
            return Characters.Where(c => c.Value.OwnerId != 0 && c.Value.CharacterType == CharacterType.Retainer && c.Key != 0 && c.Value.Name != "").ToArray();
        }

        public void LoadExistingRetainers(Dictionary<ulong, Character> characters)
        {
            PluginLog.Verbose("CharacterMonitor: Loading existing retainers");
            foreach (var character in characters)
            {
                _characters[character.Key] = character.Value;
            }
        }

        
        private ulong InternalRetainerId
        {
            get
            {
                unsafe
                {
                    var clientInterfaceUiModule = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework
                        .Instance()->UIModule->GetItemOrderModule();
                    var module = clientInterfaceUiModule;
                    if (module != null)
                    {
                        return module->ActiveRetainerId;
                    }
                    return 0;
                }
            }
        }
       
        private ulong InternalFreeCompanyId
        {
            get
            {
                unsafe
                {
                    var infoProxy = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->UIModule->GetInfoModule()->GetInfoProxyById(InfoProxyId.FreeCompany);
                    if (infoProxy != null)
                    {
                        var freeCompanyInfoProxy = (InfoProxyFreeCompany*)infoProxy;
                        return freeCompanyInfoProxy->ID;
                    }

                    return 0;
                }
            }
        }
        
        private ulong InternalCharacterId
        {
            get
            {
                unsafe
                {
                    if (Service.ClientState.LocalPlayer)
                    {
                        return Service.ClientState.LocalContentId;
                    }

                    return 0;
                }
            }
        }

        public bool IsRetainerLoaded => _isRetainerLoaded;
        public ulong ActiveRetainer => _activeRetainer;
        public ulong ActiveCharacterId => _activeCharacterId;
        public ulong ActiveFreeCompanyId => _activeFreeCompanyId;

        public Character? ActiveCharacter =>
            _characters.ContainsKey(_activeCharacterId) ? _characters[_activeCharacterId] : null;
        public uint? ActiveClassJobId => _activeClassJobId;

        public DateTime? _lastRetainerSwap;
        public DateTime? _lastCharacterSwap;
        public DateTime? _lastClassJobSwap;
        public DateTime? _lastRetainerCheck;
        public DateTime? _lastFreeCompanyCheck;
        public DateTime? _lastFreeCompanyUpdate;

        public void OverrideActiveCharacter(ulong activeCharacter)
        {
            _activeCharacterId = activeCharacter;
        }

        public void OverrideActiveRetainer(ulong activeRetainer)
        {
            _activeRetainer = activeRetainer;
        }

        public void OverrideActiveFreeCompany(ulong activeFreeCompanyId)
        {
            _activeFreeCompanyId = activeFreeCompanyId;
        }


        private void CheckRetainerId(DateTime lastUpdate)
        {
            var retainerId = this.InternalRetainerId;
            if (ActiveRetainer != retainerId)
            {
                if (_lastRetainerSwap == null)
                {
                    _isRetainerLoaded = false;
                    _activeRetainer = retainerId;
                    Service.Framework.RunOnFrameworkThread(() => { OnActiveRetainerChanged?.Invoke(ActiveRetainer); });
                    _lastRetainerSwap = lastUpdate;
                    return;
                }
            }
            var waitTime = retainerId == 0 ? 1 : 2;
            //This is the best I can come up with due it the retainer ID changing but the inventory takes almost a second to loate(I assume as it loads in from the network). This won't really take bad network conditions into account but until I can come up with a more reliable way it'll have to do
            if(_lastRetainerSwap != null && _lastRetainerSwap.Value.AddSeconds(waitTime) <= lastUpdate)
            {
                PluginLog.Verbose("CharacterMonitor: Active retainer id has changed");
                _lastRetainerSwap = null;
                //Make sure the retainer is fully loaded before firing the event
                if (retainerId != 0)
                {
                    _activeRetainer = retainerId;
                    _isRetainerLoaded = true;
                    Service.Framework.RunOnFrameworkThread(() => { OnActiveRetainerLoaded?.Invoke(ActiveRetainer); });
                }
            }

            if (_lastRetainerSwap == null && ActiveRetainer != 0 && !_isRetainerLoaded)
            {
                _isRetainerLoaded = true;
            }
        }

        private unsafe void CheckFreeCompanyId(DateTime lastUpdate)
        {
            var freeCompanyId = this.InternalFreeCompanyId;
            if (ActiveFreeCompanyId != freeCompanyId)
            {
                if (_lastFreeCompanyCheck == null)
                {
                    _isFreeCompanyLoaded = false;
                    _activeFreeCompanyId = freeCompanyId;
                    Service.Framework.RunOnFrameworkThread(() => { OnActiveFreeCompanyChanged?.Invoke(ActiveFreeCompanyId); });
                    _lastFreeCompanyCheck = lastUpdate;
                    return;
                }
            }
            var waitTime = freeCompanyId == 0 ? 1 : 2;
            
            if(_lastFreeCompanyCheck != null && _lastFreeCompanyCheck.Value.AddSeconds(waitTime) <= lastUpdate)
            {
                PluginLog.Verbose("CharacterMonitor: Active free company id has changed to " + freeCompanyId);
                _lastFreeCompanyCheck = null;
                //Make sure the retainer is fully loaded before firing the event
                if (freeCompanyId != 0)
                {
                    _activeFreeCompanyId = freeCompanyId;
                    _isFreeCompanyLoaded = true;
                    Service.Framework.RunOnFrameworkThread(() => { OnActiveFreeCompanyChanged?.Invoke(ActiveFreeCompanyId); });
                }
            }

            if (_lastFreeCompanyCheck == null && ActiveFreeCompanyId != 0 && !_isFreeCompanyLoaded)
            {
                _isFreeCompanyLoaded = true;
            }
        }
        
        private void CheckCharacterId(DateTime lastUpdate)
        {
            var characterId = InternalCharacterId;
            if ( ActiveCharacterId != characterId)
            {
                if (_lastCharacterSwap == null)
                {
                    _lastCharacterSwap = lastUpdate;
                    return;
                }
            }
            
            if(_lastCharacterSwap != null && _lastCharacterSwap.Value.AddSeconds(2) <= lastUpdate)
            {
                PluginLog.Verbose("CharacterMonitor: Active character id has changed");
                _lastCharacterSwap = null;
                //Make sure the character is fully loaded before firing the event
                if (ActiveCharacterId  != characterId)
                {
                    _activeCharacterId = characterId;
                    RefreshActiveCharacter();
                }
            }
        }
        
        
        private unsafe void UpdateRetainers(DateTime lastUpdateTime)
        {

            var retainerManager = RetainerManager.Instance();
            if (retainerManager == null)
            {
                return;
            }
            if (Service.ClientState.LocalPlayer == null || retainerManager->Ready != 1)
                return;
            if (_lastRetainerCheck == null)
            {
                _lastRetainerCheck = lastUpdateTime;
                return;
            }
            if (_lastRetainerCheck.Value.AddSeconds(2) <= lastUpdateTime)
            {
                _lastRetainerCheck = null;
                var retainerList = retainerManager->Retainer;
                var count = retainerManager->GetRetainerCount();
                var currentCharacter = Service.ClientState.LocalPlayer;
                if (currentCharacter != null)
                {
                    for (byte i = 0; i < count; ++i)
                    {
                        var retainerInformation = retainerList[i];
                        if (retainerInformation != null && retainerInformation->RetainerID != 0)
                        {
                            Character character;
                            if (_characters.ContainsKey(retainerInformation->RetainerID))
                            {
                                character = _characters[retainerInformation->RetainerID];
                            }
                            else
                            {
                                character = new Character();
                                character.CharacterId = retainerInformation->RetainerID;
                                _characters[retainerInformation->RetainerID] = character;
                            }

                            if (character.UpdateFromRetainerInformation(retainerInformation, currentCharacter, i))
                            {
                                PluginLog.Debug("Retainer " + retainerInformation->RetainerID + " was updated.");
                                character.OwnerId = Service.ClientState.LocalContentId;
                                Service.Framework.RunOnFrameworkThread(() =>
                                {
                                    OnCharacterUpdated?.Invoke(character);
                                });
                            }
                        }
                    }
                }
            }
        }
        
        
        private unsafe void UpdateFreeCompany(DateTime lastUpdateTime)
        {

            if (Service.ClientState.LocalPlayer == null)
                return;
            if (_lastFreeCompanyUpdate == null)
            {
                _lastFreeCompanyUpdate = lastUpdateTime;
                return;
            }
            if (_lastFreeCompanyUpdate.Value.AddSeconds(2) <= lastUpdateTime)
            {
                _lastFreeCompanyUpdate = null;
                var infoProxy = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->UIModule->GetInfoModule()->GetInfoProxyById(InfoProxyId.FreeCompany);
                if (infoProxy != null)
                {
                    var freeCompanyInfoProxy = (InfoProxyFreeCompany*)infoProxy;
                    var freeCompanyId = freeCompanyInfoProxy->ID;

                    if (freeCompanyId != 0)
                    {
                        Character character;
                        if (_characters.ContainsKey(freeCompanyId))
                        {
                            character = _characters[freeCompanyId];
                        }
                        else
                        {
                            character = new Character();
                            character.CharacterId = freeCompanyId;
                            _characters[freeCompanyId] = character;
                        }
                        
                        if (character.UpdateFromInfoProxyFreeCompany(freeCompanyInfoProxy))
                        {
                            PluginLog.Debug("Free Company " + character.CharacterId + " was updated.");
                            Service.Framework.RunOnFrameworkThread(() =>
                            {
                                OnCharacterUpdated?.Invoke(character);
                            });
                        }
                        else
                        {
                            
                        }
                    }
                }
            }
        }
        
        private void FrameworkOnOnUpdateEvent(Framework framework)
        {
            UpdateRetainers(framework.LastUpdate);
            UpdateFreeCompany(framework.LastUpdate);
            CheckCharacterId(framework.LastUpdate);
            CheckRetainerId(framework.LastUpdate);
            CheckFreeCompanyId(framework.LastUpdate);
            CheckCurrency(framework.LastUpdate);
            CheckCurrentClassJob(framework.LastUpdate);
        }

        private uint? CurrentClassJobId
        {
            get
            {
                if (Service.ClientState.IsLoggedIn && Service.ClientState.LocalPlayer != null)
                {
                    return Service.ClientState.LocalPlayer?.ClassJob.Id ?? null;
                }

                return null;
            }
        }

        private void CheckCurrentClassJob(DateTime frameworkLastUpdate)
        {
            var currentClassJobId = CurrentClassJobId;
            if (currentClassJobId != 0 && ActiveClassJobId != currentClassJobId)
            {
                if (_lastClassJobSwap == null)
                {
                    _lastClassJobSwap = frameworkLastUpdate;
                    return;
                }
            }
            
            if(_lastClassJobSwap != null && _lastClassJobSwap.Value.AddSeconds(1) <= frameworkLastUpdate)
            {
                PluginLog.Verbose("CharacterMonitor: Active character job has changed.");
                _lastClassJobSwap = null;
                //Make sure the character is fully loaded before firing the event
                if (ActiveClassJobId  != currentClassJobId)
                {
                    if (currentClassJobId != null && _activeClassJobId != null)
                    {
                        Service.Framework.RunOnFrameworkThread(() => { OnCharacterJobChanged?.Invoke(); });
                        RefreshActiveCharacter();
                    }
                    _activeClassJobId = currentClassJobId;
                }
            }
        }

        private void CheckCurrency(DateTime lastUpdate)
        {
        }
        
        private bool _disposed;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        private void Dispose(bool disposing)
        {
            if(!_disposed)
            {
                if(disposing)
                {
                    Service.Framework.Update -= FrameworkOnOnUpdateEvent;
                }
            }
            _disposed = true;         
        }
        
        ~CharacterMonitor()
        {
#if DEBUG
            // In debug-builds, make sure that a warning is displayed when the Disposable object hasn't been
            // disposed by the programmer.

            if( _disposed == false )
            {
                PluginLog.Error("There is a disposable object which hasn't been disposed before the finalizer call: " + (this.GetType ().Name));
            }
#endif
            Dispose (true);
        }
    }
}
