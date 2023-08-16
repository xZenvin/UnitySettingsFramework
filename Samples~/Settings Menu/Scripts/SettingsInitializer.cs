using UnityEngine;
using Zenvin.Settings.Framework;
using Zenvin.Settings.Loading;

namespace Zenvin.Settings.Samples {
	[DisallowMultipleComponent]
	public class SettingsInitializer : MonoBehaviour {

		public enum InitMode {
			Awake,
			Start
		}

		[SerializeField] private InitMode mode = InitMode.Start;
		[SerializeField] private SettingsAsset settings;
		[SerializeField] private bool resetToDefaults = true;
		[Space, SerializeField] private bool loadDynamicSettings = true;
		[SerializeField] private SettingsImportData dynamicSettings;
		[SerializeField, TextArea (25, 35), Space (20)] private string dynamicSettingsJsonOutput = "";


		private void Awake () {
			if (mode == InitMode.Awake) {
				Init ();
			}
		}

		private void Start () {
			if (mode == InitMode.Start) {
				Init ();
			}
		}


		private void Init () {
			if (loadDynamicSettings) {
				SettingsAsset.OnInitialize += LoadSettings;
			}
			if (settings != null) {
				settings.Initialize ();
				if (resetToDefaults) {
					settings.ResetAllSettings (true);
				}
			}
		}

		private void LoadSettings (SettingsAsset asset) {
			if (dynamicSettingsJsonOutput == null || dynamicSettingsJsonOutput.Length == 0) {
				return;
			}

			var options = new SettingLoaderOptions (asset)
				.WithData (dynamicSettingsJsonOutput)
				.WithSettingFactory ("bool", new BoolSettingFactory ())
				.WithSettingFactory ("int", new IntSettingFactory ())
				.WithSettingFactory ("float", new FloatSettingFactory ())
				.WithSettingFactory ("dropdown", new DropdownSettingFactory ())
				.WithSettingFactory ("slider", new SliderSettingFactory ());

			RuntimeSettingLoader.LoadSettingsIntoAsset (options);
		}

		private void OnValidate () {
			dynamicSettingsJsonOutput = JsonUtility.ToJson (dynamicSettings, true);
		}

	}
}