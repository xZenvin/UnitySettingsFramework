using System.Collections.Generic;
using Zenvin.Settings.Utility;
using UnityEngine;
using System.IO;
using System;

namespace Zenvin.Settings.Framework {
	public sealed class SettingsAsset : SettingsGroup {

		public delegate void SettingsAssetEvt (SettingsAsset asset);

		public static event SettingsAssetEvt OnInitialize;

		[NonSerialized] private readonly Dictionary<string, SettingBase> settingsDict = new Dictionary<string, SettingBase> ();
		[NonSerialized] private readonly Dictionary<string, SettingsGroup> groupsDict = new Dictionary<string, SettingsGroup> ();

		[NonSerialized] private readonly HashSet<SettingBase> dirtySettings = new HashSet<SettingBase> ();
		[NonSerialized] private bool initialized;


		public bool Initialized => initialized;
		public int RegisteredSettingsCount => settingsDict.Count;
		public int DirtySettingsCount => dirtySettings.Count;


		// Initialization

		public void Initialize () {
			if (!Initialized) {
				InitializeSettings ();
			}
		}

		private void InitializeSettings () {
			Debug.Log ("Start register  Settings.");

			RegisterSettingsRecursively (this, settingsDict, false);
			RegisterGroupsRecursively (this, groupsDict, false);

			OnInitialize?.Invoke (this);

			RegisterSettingsRecursively (this, settingsDict, true);
			RegisterGroupsRecursively (this, groupsDict, true);

			Debug.Log ($"Registered {settingsDict.Count} Settings, {groupsDict.Count} Groups.");
			initialized = true;
		}

		private void RegisterSettingsRecursively (SettingsGroup group, Dictionary<string, SettingBase> dict, bool external) {
			if (group == null) {
				return;
			}

			var settingList = external ? group.ExternalSettings : group.Settings;
			if (settingList != null) {
				foreach (var s in settingList) {
					bool canAdd = !external || !dict.ContainsKey (s.GUID);
					if (canAdd) {
						dict[s.GUID] = s;
						s.Initialize ();
					}
				}
			}

			var groupList = external ? group.ExternalGroups : group.Groups;
			if (groupList != null) {
				foreach (var g in groupList) {
					RegisterSettingsRecursively (g, dict, external);
				}
			}
		}

		private void RegisterGroupsRecursively (SettingsGroup group, Dictionary<string, SettingsGroup> dict, bool external) {
			if (group == null) {
				return;
			}

			if (!external && group == this) {
				dict[group.GUID] = group;
			} else {
				if (!external || !dict.ContainsKey (group.GUID)) {
					dict[group.GUID] = group;
				}
			}

			var groupList = external ? group.ExternalGroups : group.Groups;
			foreach (var g in groupList) {
				RegisterGroupsRecursively (g, dict, external);
			}
		}


		// Component Access

		public bool TryGetSettingByGUID (string guid, out SettingBase setting) {
			if (settingsDict.TryGetValue (guid, out setting)) {
				return true;
			}
			setting = null;
			return false;
		}

		public bool TryGetSettingByGUID<T> (string guid, out SettingBase<T> setting) where T : struct {
			if (settingsDict.TryGetValue (guid, out SettingBase sb)) {
				setting = sb as SettingBase<T>;
				return setting != null;
			}
			setting = null;
			return false;
		}

		public bool TryGetGroupByGUID (string guid, out SettingsGroup group) {
			if (guid == "") {
				group = this;
				return true;
			}
			return groupsDict.TryGetValue (guid, out group);
		}


		// Dirtying settings

		internal void SetDirty (SettingBase setting, bool dirty) {
			if (setting == null) {
				return;
			}
			if (dirty) {
				dirtySettings.Add (setting);
			} else {
				dirtySettings.Remove (setting);
			}
		}

		public void ApplyDirtySettings () {
			SettingBase[] _dirtySettings = new SettingBase[dirtySettings.Count];
			dirtySettings.CopyTo (_dirtySettings);

			foreach (var set in _dirtySettings) {
				set.ApplyValue ();
			}
		}

		public void RevertDirtySettings () {
			SettingBase[] _dirtySettings = new SettingBase[dirtySettings.Count];
			dirtySettings.CopyTo (_dirtySettings);

			foreach (var set in _dirtySettings) {
				set.RevertValue ();
			}
		}

		public void ResetAllSettings (bool apply) {
			foreach (var s in settingsDict.Values) {
				s.ResetValue (apply);
			}
		}


		// Saving & Loading

		/// <summary>
		/// Saves all settings to a stream.<br></br>
		/// Returns the number of settings saved, or -1 if there was an error.
		/// </summary>
		/// <param name="stream"> The <see cref="Stream"/> the method will write to. </param>
		public int SaveAllSettings (Stream stream) {
			if (stream == null) {
				return -1;
			}

			using (BinaryWriter writer = new BinaryWriter (stream)) {
				List<SettingData> data = new List<SettingData> (settingsDict.Count);

				foreach (SettingBase set in settingsDict.Values) {
					if (set != null && set.TrySerialize (out SettingData sd)) {
						data.Add (sd);
					}
				}

				writer.Write (data.Count);
				for (int i = 0; i < data.Count; i++) {
					writer.Write (data[i].GUID);
					writer.WriteArray (data[i].Data);
				}

				return data.Count;
			}
		}

		/// <summary>
		/// Loads all settings from a stream.<br></br>
		/// Returns the number of settings loaded, or -1 if there was an error.
		/// </summary>
		/// <param name="reader"> The <see cref="Stream"/> the method will read from. </param>	
		public int LoadAllSettings (Stream stream) {
			if (!Initialized || stream == null || stream.Length - stream.Position == 0) {
				return -1;
			}

			using (BinaryReader reader = new BinaryReader (stream)) {
				int loaded = 0;

				int count = reader.ReadInt32 ();
				for (int i = 0; i < count; i++) {
					string guid = reader.ReadString ();
					byte[] data = reader.ReadArray ();

					if (settingsDict.TryGetValue (guid, out SettingBase setting)) {
						setting.Deserialize (data);
						loaded++;
					}
				}

				return loaded;
			}
		}


		// Utility

		internal bool Editor_IsValidGUID (string guid, bool isGroup) {
			if (Initialized) {
				return false;
			}
			if (string.IsNullOrWhiteSpace (guid)) {
				return false;
			}

			HashSet<string> guids = new HashSet<string> ();
			guids.Add (guid);

			if (isGroup) {
				var allGroups = GetAllGroups ();
				foreach (var g in allGroups) {
					if (!guids.Add (g.GUID)) {
						return false;
					}
				}
			} else {
				var allSettings = GetAllSettings ();
				foreach (var s in allSettings) {
					if (!guids.Add (s.GUID)) {
						return false;
					}
				}
			}

			return true;
		}

		internal bool IsValidGuid (string guid, bool isGroup) {
			if (!Application.isPlaying) {
				return false;
			}
			if (string.IsNullOrWhiteSpace (guid)) {
				return false;
			}
			return isGroup ? !groupsDict.ContainsKey (guid) : !settingsDict.ContainsKey (guid);
		}

	}
}