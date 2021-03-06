using Object = UnityEngine.Object;

using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using System;
using UnityEditor.Callbacks;

namespace Zenvin.Settings.Framework {
	internal class SettingsEditorWindow : EditorWindow, ISerializationCallbackReceiver {

		[Flags]
		internal enum HierarchyFilter : int {
			/// <summary> Settings created during edit time. </summary>
			Normal = 0b0001,
			/// <summary> Settings loaded during runtime. </summary>
			External = 0b0010,
			/// <summary> Dirty settings. </summary>
			Dirty = 0b0100,
			/// <summary> Non-dirty settings. </summary>
			Clean = 0b1000,

			All = ~0b0000
		}

		private const float indentSize = 15f;
		private const float margin = 1f;

		private static readonly Color hierarchyColorA = /*new Color (0.3f, 0.3f, 0.3f)*/new Color (0f, 0f, 0f, 0f);
		private static readonly Color hierarchyColorB = new Color (0.1f, 0.1f, 0.1f, 0f);
		private static readonly Color hierarchyColorSelected = new Color (0f, 0.5f, 1.0f, 0.4f);
		private static readonly Color hierarchyColorDragged = new Color (1f, 0.5f, 1f, 0.4f);
		private static readonly Color hierarchyColorHover = new Color (0.3f, 0.3f, 0.3f);

		private static GUIStyle foldoutStyleInternal;
		private static GUIStyle foldoutStyleExternal;
		private static GUIStyle labelStyleInternal;
		private static GUIStyle labelStyleExternal;


		[SerializeField] private Texture windowIcon;

		[SerializeField, HideInInspector] private List<SettingsGroup> hierarchyState;

		[SerializeField] private SettingsAsset asset;
		private SettingsAsset[] allAssets = null;

		//private float hierarchyWidth = 200f;
		private float hierarchyWidth = 300f;

		[NonSerialized] private string searchString = string.Empty;
		private List<SettingBase> searchResults = null;
		private HierarchyFilter searchFilter = HierarchyFilter.All;

		private ScriptableObject selected = null;
		[NonSerialized] private ScriptableObject dragged = null;
		[NonSerialized] private ScriptableObject hovered = null;
		[NonSerialized] private Rect? dragPreview;

		private readonly Dictionary<SettingsGroup, bool> expansionState = new Dictionary<SettingsGroup, bool> ();
		private Vector2 hierarchyScroll;
		private Vector2 editorScroll;
		private Vector2 assetSelectScroll;
		private Editor editor = null;

		private Type[] viableSettingTypes = null;
		private Type[] viableGroupTypes = null;


		private HierarchyFilter CurrentFilter => Application.isPlaying ? searchFilter : HierarchyFilter.All;
		private bool AllowDrag => searchResults == null;


		// Menus

		//[MenuItem ("Assets/Create/Scriptable Objects/Zenvin/Settings Asset", priority = -10000)]
		//private static SettingsAsset HandleCreateSettingsAsset () {
		//	SettingsAsset asset = FindAsset ();

		//	if (asset == null) {
		//		asset = CreateInstance<SettingsAsset> ();
		//		asset.name = "Game Settings";
		//		asset.Name = "Game Settings";

		//		AssetDatabase.CreateAsset (asset, $"Assets/Game Settings.asset");

		//		AssetDatabase.Refresh ();
		//		AssetDatabase.SaveAssets ();
		//	} else {
		//		Selection.activeObject = asset;
		//	}

		//	return asset;
		//}

		[MenuItem ("Window/Zenvin/Settings Asset Editor")]
		private static void Init () {
			InitWindow ();
		}

		private static void Init (SettingsAsset edit) {
			InitWindow ().asset = edit;
		}

		private static SettingsEditorWindow InitWindow () {
			SettingsEditorWindow win = GetWindow<SettingsEditorWindow> ();
			win.titleContent = new GUIContent ("Settings Editor", win.windowIcon);
			win.minSize = new Vector2 (500f, 200f);
			win.Show ();
			return win;
		}

		[OnOpenAsset]
		private static bool HandleOpenAsset (int instanceID, int line) {
			Object obj = EditorUtility.InstanceIDToObject (instanceID);
			if (obj is SettingsAsset asset) {
				Init (asset);
				return true;
			}
			return false;
		}


		// Initialization

		private void OnEnable () {
			hierarchyWidth = EditorPrefs.GetFloat ($"{GetType ().FullName}.{nameof (hierarchyWidth)}", 200f);
			ExpandToSelection (false);
		}

		private void SetupStyles () {
			foldoutStyleInternal = EditorStyles.foldout;
			foldoutStyleExternal = new GUIStyle (EditorStyles.foldout) { fontStyle = FontStyle.Italic };
			labelStyleInternal = EditorStyles.label;
			labelStyleExternal = new GUIStyle (EditorStyles.label) { fontStyle = FontStyle.Italic };
		}


		// GUI methods

		private void OnGUI () {
			Event _evt = Event.current;
			if (_evt.type == EventType.KeyDown && _evt.keyCode == KeyCode.RightShift) {
				asset = null;
			}

			// prompt setting asset selection if there is none
			if (asset == null) {
				DrawAssetMenu ();
				Repaint ();
				return;
			}

			// make sure styles are set up
			if (foldoutStyleInternal == null || foldoutStyleExternal == null || labelStyleInternal == null || labelStyleExternal == null) {
				SetupStyles ();
			}

			if (asset.ChildGroupCount == 0 && asset.SettingCount == 0) {
				Select (asset);
			}

			// create rects for windows partitions
			Rect topBarRect = new Rect (
				margin, margin, position.width - margin * 2f, EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing + 1
			);
			Rect hierarchyRect = new Rect (
				margin, margin + topBarRect.height + margin, hierarchyWidth - margin * 2f, position.height - margin * 2f - topBarRect.height
			);
			Rect editorRect = new Rect (
				hierarchyWidth + 2f + margin * 2f, margin + topBarRect.height + margin, position.width - hierarchyWidth - 2f - margin * 2f, position.height - margin * 3f - topBarRect.height
			);

			// draw draggable line between hierarchy and inspector
			////if (!hierarchyWidthLocked) {
			////	Rect dragRect = new Rect (hierarchyWidth, topBarRect.height, 2f, position.height - topBarRect.height);
			////	if (MakeHorizontalDragRect (ref dragRect, 200f, position.width - 300f)) {
			////		hierarchyWidth = dragRect.x;
			////		EditorPrefs.SetFloat ($"{GetType ().FullName}.{nameof (hierarchyWidth)}", hierarchyWidth);
			////		Repaint ();
			////	}
			////}

			Rect separatorRect = new Rect (hierarchyWidth, topBarRect.height, 1f, position.height - topBarRect.height);
			EditorGUI.DrawRect (separatorRect, new Color (0.1f, 0.1f, 0.1f));

			// draw window partition contents
			DrawTopBar (topBarRect);
			DrawHierarchy (hierarchyRect);
			DrawEditor (editorRect);

			// update window frequently if a hierarchy item is being dragged
			if (dragged != null && dragPreview.HasValue) {
				EditorGUI.DrawRect (dragPreview.Value, Color.blue);
				Repaint ();
			}

			// handle drag escaping
			Event e = Event.current;
			if (dragged != null) {
				if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) {
					Select (dragged);
					HandleEndDrag ();
					e.Use ();
				}
			}

			EditorUtility.SetDirty (asset);
		}


		private void DrawAssetMenu () {
			//Vector2 btnSize = new Vector2 (250f, 50f);
			//float xOffset = (position.width - btnSize.x) * 0.5f;
			//float yOffset = (position.height - btnSize.y) * 0.5f;

			//Rect btnRect = new Rect (xOffset, yOffset, btnSize.x, btnSize.y);

			//if (GUI.Button (btnRect, "Create Settings Asset")) {
			//	//asset = HandleCreateSettingsAsset ();
			//}

			const float menuWidth = 450f;
			const float verticalMargin = 20f;
			Rect menuRect = new Rect ((position.width - menuWidth) * 0.5f, verticalMargin, menuWidth, position.height - verticalMargin * 2f);

			GUILayout.BeginArea (menuRect);

			GUILayout.Label ("Select Settings Asset", EditorStyles.boldLabel);
			assetSelectScroll = GUILayout.BeginScrollView (assetSelectScroll, false, false);

			if (allAssets == null || allAssets.Length == 0) {
				LoadSettingsAssets ();
			}

			for (int i = 0; i < allAssets.Length; i++) {
				// create the rect for this hierarchy item
				Rect rect = EditorGUILayout.GetControlRect ();
				//rect.x -= 2f;
				//rect.width += 4;
				//rect.y -= 2f;
				//rect.height += 2f;

				// draw coloured background
				Color col = rect.Contains (Event.current.mousePosition) ? hierarchyColorHover : hierarchyColorA;
				EditorGUI.DrawRect (rect, col);

				// draw select button
				if (GUI.Button (rect, allAssets[i].name, EditorStyles.label)) {
					asset = allAssets[i];
					GUILayout.EndScrollView ();
					GUILayout.EndArea ();
					return;
				}
			}

			GUILayout.FlexibleSpace ();

			// draw refresh button
			if (GUILayout.Button ("Refresh")) {
				LoadSettingsAssets ();
			}

			GUILayout.Space (10);

			// draw create asset button
			if (GUILayout.Button ("Create new Asset")) {
				string path = EditorUtility.SaveFilePanelInProject ("Create Settings Asset", "New Settings", "asset", "");
				if (string.IsNullOrEmpty (path)) {
					GUILayout.EndArea ();
					return;
				}
				asset = CreateInstance<SettingsAsset> ();
				AssetDatabase.CreateAsset (asset, path);
				AssetDatabase.SaveAssetIfDirty (asset);
				AssetDatabase.Refresh ();
				return;
			}


			GUILayout.EndScrollView ();
			GUILayout.EndArea ();
		}

		private void DrawTopBar (Rect rect) {
			Rect bar = new Rect (rect);
			bar.height -= 1f;

			Rect line = new Rect (rect);
			line.height = 1f;
			line.y += bar.height;

			EditorGUI.DrawRect (line, new Color (0.1f, 0.1f, 0.1f));

			GUILayout.BeginArea (bar);

			GUILayout.BeginHorizontal ();

			DrawSearchBar (hierarchyWidth);

			SettingsGroup selGroup = selected as SettingsGroup;
			bool canAdd = selGroup != null;

			GUI.enabled = canAdd;
			Rect addGroupBtnRect = EditorGUILayout.GetControlRect (false, GUILayout.Width (150));
			if (GUI.Button (addGroupBtnRect, "Add Group"/*, GUILayout.Width (150), GUILayout.Height (EditorGUIUtility.singleLineHeight)*/) && !Application.isPlaying) {
				//CreateGroupAsChildOfGroup (selected);
				GenericMenu gm = new GenericMenu ();
				PopulateGroupsTypeMenu (gm, selGroup, false);
				gm.DropDown (addGroupBtnRect);
			}

			Rect addSettingBtnRect = EditorGUILayout.GetControlRect (false, GUILayout.Width (150));
			if (GUI.Button (addSettingBtnRect, "Add Setting") && !Application.isPlaying) {
				GenericMenu gm = new GenericMenu ();
				PopulateSettingTypeMenu (gm, selGroup, false);
				gm.DropDown (addSettingBtnRect);
			}
			GUI.enabled = true;

			if (GUILayout.Button ("Select Asset", GUILayout.Width (150), GUILayout.Height (EditorGUIUtility.singleLineHeight))) {
				Selection.activeObject = asset;
			}

			GUILayout.EndHorizontal ();

			GUILayout.EndArea ();
		}

		private void DrawHierarchy (Rect rect) {
			GUILayout.BeginArea (rect);
			hierarchyScroll = EditorGUILayout.BeginScrollView (hierarchyScroll, false, false);

			// reset hovered object and drag preview rect
			hovered = null;
			dragPreview = null;

			// display normal hierarchy
			if (searchResults == null) {
				int index = 0;
				DrawSettingsGroup (asset, 0, ref index);

				// display hierarchy based on search results
			} else {
				for (int i = 0; i < searchResults.Count; i++) {
					DrawSetting (searchResults[i], 0, i, i);
				}
			}

			EditorGUILayout.EndScrollView ();


			// draw preview rect if necessary
			if (dragPreview != null) {
				EditorGUI.DrawRect (dragPreview.Value, Color.blue);
			}

			EditorGUILayout.LabelField (hierarchyWidth + " | " + (position.width - hierarchyWidth));

			GUILayout.EndArea ();
		}

		private void DrawEditor (Rect rect) {
			// return if current editor is invalid
			if (editor == null || editor.target == null || editor.serializedObject == null) {
				return;
			}

			GUILayout.BeginArea (rect);
			// make read-only while in play mode
			EditorGUI.BeginDisabledGroup (Application.isPlaying);

			if (editor.target == asset) {
				EditorGUILayout.LabelField ("GUID", "None (Root)");
			} else {
				DrawGuidField (editor.serializedObject, true);
			}

			GUILayout.Space (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
			DrawPropertyField (editor.serializedObject.FindProperty ("label"), new GUIContent ("Name"));
			DrawPropertyField (editor.serializedObject.FindProperty ("labelLocalizationKey"), new GUIContent ("Name Loc. Key"));

			GUILayout.Space (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
			DrawTextArea ("Description", editor.serializedObject.FindProperty ("description"), rect);
			EditorGUILayout.PropertyField (editor.serializedObject.FindProperty ("descriptionLocalizationKey"), new GUIContent ("Description Loc. Key"));

			GUILayout.Space (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);

			// draw editor based on type of selected object
			switch (editor.target) {
				case SettingsGroup g:
					DrawDefaultEditor (g);
					break;
				case SettingBase s:
					DrawDefaultEditor (s);
					break;
				default:
					return;
			}

			GUILayout.Space (10);

			editorScroll = EditorGUILayout.BeginScrollView (editorScroll, false, false);
			if (Application.isPlaying) {

				// draw read-only runtime information
				if (editor.target is SettingBase s) {
					EditorGUILayout.LabelField ("Default Value", s.DefaultValueRaw?.ToString () ?? "", EditorStyles.textField);
					EditorGUILayout.LabelField ("Current Value", s.CurrentValueRaw?.ToString () ?? "", EditorStyles.textField);
					EditorGUILayout.LabelField ("Cached Value", s.CachedValueRaw?.ToString () ?? "", EditorStyles.textField);
					EditorGUILayout.LabelField ("Is Dirty", s.IsDirty.ToString (), EditorStyles.textField);

					GUILayout.Space (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
					EditorGUILayout.LabelField ("Order in Group", s.OrderInGroup.ToString (), EditorStyles.textField);
				}

			} else {

				// draw default inspector to allow for custom properties in settings
				editor.DrawDefaultInspector ();
				editor.serializedObject.ApplyModifiedProperties ();
				EditorUtility.SetDirty (editor.target);

			}
			EditorGUILayout.EndScrollView ();

			EditorGUI.EndDisabledGroup ();
			GUILayout.EndArea ();
		}

		private void DrawDefaultEditor (SettingsGroup group) {

			// as long as the selected object is not the root asset
			//if (group != asset) {
			//	// draw GUID field
			//	DrawGuidField (editor.serializedObject, true);
			//} else {
			//	EditorGUILayout.LabelField ("GUID", "None (Root)");
			//}

			//GUILayout.Space (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);

			// draw base properties separate from inspector
			//EditorGUILayout.PropertyField (editor.serializedObject.FindProperty (/*nameof (SettingsGroup.groupName)*/"label"), new GUIContent ("Name"));
			//EditorGUILayout.PropertyField (editor.serializedObject.FindProperty (/*nameof (SettingsGroup.groupNameLocKey)*/"labelLocalizationKey"), new GUIContent ("Loc. Key"));
			EditorGUILayout.PropertyField (editor.serializedObject.FindProperty (nameof (SettingsGroup.groupIcon)), new GUIContent ("Icon"));

			// display runtime information on root asset
			if (group == asset && Application.isPlaying) {
				GUILayout.Space (10);
				EditorGUILayout.LabelField ("Registered Groups", asset.RegisteredGroupsCount.ToString (), EditorStyles.textField);
				EditorGUILayout.LabelField ("Registered Settings", asset.RegisteredSettingsCount.ToString (), EditorStyles.textField);
				EditorGUILayout.LabelField ("Dirty Settings", asset.DirtySettingsCount.ToString (), EditorStyles.textField);
			}

			if (!Application.isPlaying) {
				editor.serializedObject.ApplyModifiedProperties ();
			}
		}

		private void DrawDefaultEditor (SettingBase setting) {
			//DrawGuidField (editor.serializedObject, false);

			//GUILayout.Space (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);

			//EditorGUILayout.PropertyField (editor.serializedObject.FindProperty (/*"settingName"*/"label"), new GUIContent ("Name"));
			//EditorGUILayout.PropertyField (editor.serializedObject.FindProperty (/*"settingNameLocKey"*/"labelLocalizationKey"), new GUIContent ("Loc. Key"));

			//if (!Application.isPlaying) {
			//	editor.serializedObject.ApplyModifiedProperties ();
			//}
		}


		// Special field drawers

		private void DrawSearchBar (float? width = null) {

			string search = searchString;

			// draw search field
			searchString = EditorGUILayout.DelayedTextField (searchString, EditorStyles.toolbarSearchField, GUILayout.Width (width ?? 100f));

			// if application is playing, enable setting filtering
			// during edit-time there are not enough relevant, differentiating properties to warrant filtering.
			if (Application.isPlaying) {
				searchFilter = (HierarchyFilter)EditorGUILayout.EnumFlagsField (searchFilter, GUILayout.Width (150));
			}

			// reset search results if search string is empty
			if (string.IsNullOrEmpty (searchString)) {
				searchResults = null;
				Repaint ();
				return;
			}

			// update search results if search string has changed
			if (search != searchString) {
				Select (null);
				var settings = asset.GetAllSettings ();
				searchResults = new List<SettingBase> ();
				foreach (var s in settings) {
					if (s.Name.ToUpperInvariant ().Contains (searchString.ToUpperInvariant ())) {
						searchResults.Add (s);
					}
				}
			}
		}

		private void DrawGuidField (SerializedObject obj, bool group) {
			if (obj == null) {
				return;
			}

			// find the GUID property of the selected object
			SerializedProperty guidProp = obj.FindProperty ("guid");
			if (guidProp == null) {
				return;
			}

			// draw GUID input field
			string val = guidProp.stringValue;
			string newVal = EditorGUILayout.DelayedTextField ("GUID", val);
			if (newVal == val) {
				return;
			}

			// validate GUID
			if (!asset.Editor_IsValidGUID (newVal, group)) {
				newVal = val;
			}

			// update GUID value
			guidProp.stringValue = newVal;
			obj.ApplyModifiedPropertiesWithoutUndo ();
		}

		private void DrawPropertyField (SerializedProperty property, GUIContent label) {
			EditorGUILayout.PropertyField (property, label);
			property.serializedObject.ApplyModifiedProperties ();
		}

		private void DrawTextArea (string label, SerializedProperty property, Rect rect) {
			if (property.propertyType != SerializedPropertyType.String) {
				return;
			}
			EditorGUILayout.LabelField (label);

			string val = property.stringValue;
			float height = EditorStyles.textArea.CalcHeight (new GUIContent (val), rect.width);

			val = EditorGUILayout.TextArea (val, GUILayout.Height (Mathf.Max (height, EditorGUIUtility.singleLineHeight * 3f)));

			if (property.stringValue != val) {
				property.stringValue = val;
				property.serializedObject.ApplyModifiedProperties ();
			}
		}


		// Hierarchy drawers

		private void DrawSettingsGroup (SettingsGroup group, int groupIndex, ref int index, int indent = 0) {
			if (group == null) {
				return;
			}

			index++;

			// create the rect for this hierarchy item
			Rect rect = EditorGUILayout.GetControlRect ();
			rect.x -= 2f;
			rect.width += 4;
			rect.y -= 2f;
			rect.height += 2f;

			// draw coloured background
			Color col = GetHierarchyColor (group, index, rect.Contains (Event.current.mousePosition));
			EditorGUI.DrawRect (rect, col);

			// calculate indented rect
			Rect lblRect = new Rect (rect);
			lblRect.x += indentSize * (indent + 1);
			lblRect.width -= indentSize * (indent + 1);

			// toggle expansion state
			if (!expansionState.TryGetValue (group, out bool expanded)) {
				expanded = false;
			}

			float indentValue = indent * indentSize + 10f;
			Rect foldRect = new Rect (rect.x + indentValue, rect.y, rect.width - indentValue, rect.height);

			if ((group.ChildGroupCount + group.SettingCount) > 0) {
				expanded = EditorGUI.Foldout (foldRect, expanded, group.Name, false, group.External ? foldoutStyleExternal : foldoutStyleInternal);
				expansionState[group] = expanded;
			} else {
				foldRect.x += indentSize;
				foldRect.width -= indentSize;
				EditorGUI.LabelField (foldRect, group.Name);
			}

			// input handling
			Event e = Event.current;
			if (lblRect.Contains (e.mousePosition)) {
				hovered = group;
				//int? q = GetCursorQuadrant (rect, e.mousePosition, 0.3f);
				bool below = e.mousePosition.y > (rect.y + rect.height * 0.8f);

				switch (e.type) {
					//case EventType.ContextClick:
					//	e.Use ();
					//	Select (group);
					//	ShowGroupMenu (group);
					//	break;
					case EventType.MouseDown:
						if (e.button == 1) {
							Select (group);
							ShowGroupMenu (group);
						} else if (dragged == null) {
							e.Use ();
							Select (group);
						}
						break;
					case EventType.MouseDrag:
						if (AllowDrag && e.button == 0) {
							Select (null);
							if (dragged == null && group != asset) {
								e.Use ();
								dragged = group;
							}
						}
						break;
					case EventType.MouseUp:
						if (AllowDrag) {
							//HandleEndDrag (group, groupIndex, q);
							HandleEndDrag (group, groupIndex, below);
							e.Use ();
						}
						break;
				}

				if (dragged != null && dragged != group/* && q.HasValue*/ && below) {

					Rect preview = new Rect (rect);
					preview.y += rect.height * 0.8f;
					preview.height = rect.height * 0.2f;
					EditorGUI.DrawRect (preview, Color.blue);

					//	int _q = q.Value;

					//	float prevHeight = 2f;
					//	float prevOffset = prevHeight * 0.5f;
					//	Rect preview = new Rect (rect);
					//	switch (_q) {
					//		case -1:
					//			preview.y -= prevOffset;
					//			preview.height = prevHeight;
					//			dragPreview = preview;
					//			break;
					//		case 1:
					//			preview.y += preview.height - prevOffset;
					//			preview.height = prevHeight;
					//			dragPreview = preview;
					//			break;
					//		default:
					//			dragPreview = null;
					//			break;
					//	}
				}
			}

			// don't draw children if item is not expanded in the hierarchy
			if (!expanded) {
				return;
			}

			// draw sub-groups
			for (int i = 0; i < group.ChildGroupCount; i++) {
				DrawSettingsGroup (group.GetGroupAt (i), i, ref index, indent + 1);
			}

			// draw settings from current group
			DrawGroupSettings (group, ref index, indent + 1);

		}

		private void DrawGroupSettings (SettingsGroup group, ref int index, int indent) {

			for (int i = 0; i < group.SettingCount; i++) {

				SettingBase setting = group.GetSettingAt (i);
				if (setting == null) {
					continue;
				}

				// only show setting if current filter matches setting properties.
				// during edit-time, CurrentFilter will always be HierarchyFilter.All
				var filter = CurrentFilter;
				if (((filter & HierarchyFilter.Clean) != 0 && !setting.IsDirty) ||
					((filter & HierarchyFilter.Dirty) != 0 && setting.IsDirty) ||
					((filter & HierarchyFilter.Normal) != 0 && !setting.External) ||
					((filter & HierarchyFilter.External) != 0 && setting.External)) {

					index++;
					DrawSetting (setting, indent, index, i);

				}
			}

		}

		private void DrawSetting (SettingBase setting, int indent, int index, int settingIndex) {

			// calculate rect for this hierarchy item
			Rect rect = EditorGUILayout.GetControlRect ();
			rect.x -= 2f;
			rect.width += 4;
			rect.y -= 2f;
			rect.height += 2f;

			// draw coloured background
			Color col = GetHierarchyColor (setting, index, rect.Contains (Event.current.mousePosition));
			EditorGUI.DrawRect (rect, col);

			// calculate indented rect & display label
			float indentValue = (indent + 1) * indentSize + 10f;
			Rect foldRect = new Rect (rect.x + indentValue, rect.y, rect.width - indentValue, rect.height);
			EditorGUI.LabelField (foldRect, $"{setting.Name} ({setting.ValueType.Name})", setting.External ? labelStyleExternal : labelStyleInternal);

			// input handling
			Event e = Event.current;
			if (foldRect.Contains (e.mousePosition)) {
				hovered = setting;

				switch (e.type) {
					case EventType.MouseDown:
						e.Use ();
						Select (setting);
						if (e.button == 1) {
							ShowSettingMenu (setting);
						}
						break;
					//case EventType.ContextClick:
					//	e.Use ();
					//	Select (setting);
					//	ShowSettingMenu (setting);
					//	break;
					case EventType.MouseDrag:
						if (AllowDrag && e.button == 0) {
							Select (null);
							if (dragged == null) {
								e.Use ();
								dragged = setting;
							}
						}
						break;
					case EventType.MouseUp:
						if (AllowDrag) {
							HandleEndDrag (setting, settingIndex);
							e.Use ();
						}
						break;
				}
			}
		}


		// Hierarchy manipulation

		private void MoveGroup (SettingsGroup dragged, SettingsGroup settingsGroup, int groupIndex) {
			settingsGroup?.AddChildGroup (dragged, groupIndex);
		}

		private void MoveSetting (SettingBase dragged, SettingsGroup settingsGroup, int index) {
			settingsGroup.AddSetting (dragged, index);
		}

		private void Select (ScriptableObject sel) {
			if (sel == selected) {
				return;
			}

			// reset GUI focus
			GUI.FocusControl ("");

			// update editor
			if (sel == null) {
				editor = null;
			} else {
				editor = Editor.CreateEditor (sel);
			}

			selected = sel;
			Repaint ();
		}

		private void HandleEndDrag () {
			dragged = null;
		}

		private void HandleEndDrag (ScriptableObject hover, int index, bool below = false) {
			ScriptableObject _dragged = dragged;
			dragged = null;

			if (_dragged == null || hovered == null || Application.isPlaying) {
				return;
			}

			if (_dragged == hover) {
				return;
			}

			SettingsGroup dGroup = _dragged as SettingsGroup;
			SettingsGroup hGroup = hovered as SettingsGroup;
			SettingBase dSetting = _dragged as SettingBase;
			SettingBase hSetting = hovered as SettingBase;

			// dragging group onto group
			if (dGroup != null && hGroup != null) {
				if (below) {
					MoveGroup (dGroup, hGroup.Parent ?? asset, index);
				} else {
					MoveGroup (dGroup, hGroup, 0);
				}

				// dragging setting onto group
			} else if (dSetting != null && hGroup != null) {
				MoveSetting (dSetting, hGroup, 0);

				// dragging setting onto setting
			} else if (dSetting != null && hSetting != null) {
				MoveSetting (dSetting, hSetting.group, index);

				// dragging group onto setting
			} else if (dGroup != null && dSetting != null) {
				MoveGroup (dGroup, dSetting.group, dSetting.group.ChildGroupCount);

			}
		}


		// Context Menu creation

		private void ShowGroupMenu (SettingsGroup group) {
			GenericMenu gm = new GenericMenu ();

			// only allow changing menus to be shown during edit-time
			if (!Application.isPlaying) {

				PopulateSettingTypeMenu (gm, group);
				//gm.AddItem (new GUIContent ("Add Group"), false, CreateGroupAsChildOfGroup, group);
				PopulateGroupsTypeMenu (gm, group);

				if (group != asset) {
					gm.AddSeparator ("");
					gm.AddItem (new GUIContent ("Delete Group"), false, DeleteGroup, group);
				}

				gm.AddSeparator ("");
			}

			gm.AddItem (new GUIContent ("Collapse/All"), false, () => {
				Select (asset);
				expansionState.Clear ();
				expansionState[asset] = true;
			});
			gm.AddItem (new GUIContent ("Collapse/To This"), false, () => { ExpandToSelection (true); });

			gm.ShowAsContext ();
		}

		private void PopulateSettingTypeMenu (GenericMenu gm, SettingsGroup group, bool prefixItem = true) {
			if (gm == null || group == null) {
				return;
			}

			// load all types inheriting SettingBase<T>
			// result will be cached automatically
			LoadViableTypes ();

			// there are no viable types to create settings from
			if (viableSettingTypes.Length == 0) {
				gm.AddDisabledItem (prefixItem ? new GUIContent ("Add Setting") : new GUIContent ("No Setting types found."));

				// generate sub-menus for each viable type
			} else {
				string prefix = prefixItem ? "Add Setting/" : "";
				foreach (Type t in viableSettingTypes) {
					Type generic = null;
					Type[] generics = t.BaseType.GetGenericArguments ();
					if (generics.Length > 0) {
						generic = generics[0];
					}
					string ns = string.IsNullOrEmpty (t.Namespace) ? "<Global>" : t.Namespace;
					gm.AddItem (
						new GUIContent ($"{prefix}{ns}/{ToEditorSpelling (t.Name)}"),
						false, CreateSettingAsChildOfGroup, new NewSettingData (t, group)
					);
				}
			}
		}

		private void PopulateGroupsTypeMenu (GenericMenu gm, SettingsGroup group, bool prefixItem = true) {
			if (gm == null || group == null) {
				return;
			}

			// load all types inheriting SettingsGroup
			// result will be cached automatically
			LoadViableTypes ();

			string prefix = prefixItem ? "Add Group/" : "";

			gm.AddItem (new GUIContent (prefixItem ? "Add Default Group" : "Default Group"), false, CreateGroupAsChildOfGroup, new NewSettingData (typeof (SettingsGroup), group));

			if (viableGroupTypes.Length > 0) {
				if (!prefixItem) {
					gm.AddSeparator ("");
				}
				foreach (Type t in viableGroupTypes) {
					Type generic = null;
					Type[] generics = t.BaseType.GetGenericArguments ();
					if (generics.Length > 0) {
						generic = generics[0];
					}
					string ns = string.IsNullOrEmpty (t.Namespace) ? "<Global>" : t.Namespace;
					gm.AddItem (
						new GUIContent ($"{prefix}{ns}/{ToEditorSpelling (t.Name)}"),
						false, CreateGroupAsChildOfGroup, new NewSettingData (t, group)
					);
				}
			}
		}

		private void ShowSettingMenu (SettingBase setting) {
			GenericMenu gm = new GenericMenu ();

			if (!Application.isPlaying) {
				gm.AddItem (new GUIContent ("Duplicate Setting"), false, DuplicateSetting, setting);
				gm.AddSeparator ("");
				gm.AddItem (new GUIContent ("Delete Setting"), false, DeleteSetting, setting);
				gm.AddSeparator ("");
			}

			gm.AddItem (new GUIContent ("Collapse/All"), false, () => {
				Select (asset);
				expansionState.Clear ();
				expansionState[asset] = true;
			});
			gm.AddItem (new GUIContent ("Collapse/To This"), false, () => { ExpandToSelection (true); });

			gm.ShowAsContext ();
		}


		// Creating and deleting Settings & Groups

		private void CreateSettingAsChildOfGroup (object data) {
			if (!(data is NewSettingData d)) {
				return;
			}
			if (d.Group == null) {
				return;
			}

			SettingBase setting = AssetUtility.CreateAsPartOf<SettingBase> (asset, d.SettingType, s => {
				s.name = "New Setting";
				s.Name = "New Setting";
				s.GUID = Guid.NewGuid ().ToString ();
				s.asset = asset;
			});

			d.Group.AddSetting (setting);
			Select (setting);
			ExpandToSelection (false);
		}

		private void CreateGroupAsChildOfGroup (object group) {
			//if (!(group is SettingsGroup g)) {
			//	return;
			//}

			//SettingsGroup newGroup = AssetUtility.CreateAsPartOf<SettingsGroup> (asset, g => {
			//	g.name = "New Group";
			//	g.Name = "New Group";
			//	g.GUID = Guid.NewGuid ().ToString ();
			//});

			//g.AddChildGroup (newGroup);
			//Select (newGroup);
			//ExpandToSelection (false);

			SettingsGroup parentGroup;
			SettingsGroup newGroup;

			switch (group) {
				case SettingsGroup g:
					parentGroup = g;
					newGroup = AssetUtility.CreateAsPartOf<SettingsGroup> (asset, g => {
						g.name = "New Group";
						g.Name = "New Group";
						g.GUID = Guid.NewGuid ().ToString ();
					});

					g.AddChildGroup (newGroup);
					Select (newGroup);
					ExpandToSelection (false);
					break;
				case NewSettingData nsd:
					parentGroup = nsd.Group;
					newGroup = AssetUtility.CreateAsPartOf<SettingsGroup> (asset, nsd.SettingType, g => {
						g.name = "New Group";
						g.Name = "New Group";
						g.GUID = Guid.NewGuid ().ToString ();
					});
					break;
				default:
					return;
			}

			parentGroup.AddChildGroup (newGroup);
			Select (newGroup);
			ExpandToSelection (false);
		}

		private void DuplicateSetting (object setting) {
			if (!(setting is SettingBase set)) {
				return;
			}

			SettingBase newSetting = Instantiate (set);
			newSetting.GUID = Guid.NewGuid ().ToString ();

			AssetDatabase.AddObjectToAsset (newSetting, asset);
			AssetDatabase.SaveAssets ();
			AssetDatabase.Refresh ();

			newSetting.group = null;
			set.group.AddSetting (newSetting);
			Select (newSetting);
		}

		private void DeleteGroup (object group) {
			if (group is SettingsGroup g) {
				if (selected == g) {
					Select (g.Parent);
				}

				for (int i = g.ChildGroupCount - 1; i >= 0; i--) {
					asset.AddChildGroup (g.GetGroupAt (i));
				}
				for (int i = g.SettingCount - 1; i >= 0; i--) {
					asset.AddSetting (g.GetSettingAt (i));
				}
				expansionState.Remove (g);
				if (g.Parent != null) {
					g.Parent.RemoveChildGroup (g);
				}
				DestroyImmediate (g, true);
				AssetDatabase.Refresh ();
				AssetDatabase.SaveAssets ();
				Repaint ();
			}
		}

		private void DeleteSetting (object setting) {
			if (setting is SettingBase s) {
				s.group.RemoveSetting (s);

				DestroyImmediate (s, true);
				AssetDatabase.Refresh ();
				AssetDatabase.SaveAssets ();
				Repaint ();
			}
		}


		// Helper Methods

		public static bool MakeHorizontalDragRect (ref Rect rect, float min, float max, float size = 5f, Color? color = null) {
			Color col = color.HasValue ? color.Value : new Color (0.1f, 0.1f, 0.1f);

			EditorGUI.DrawRect (rect, col);
			Event e = Event.current;

			if (e.type == EventType.Used) {
				return false;
			}

			Rect r = new Rect (rect);
			r.x -= size * 0.5f;
			r.width = Mathf.Abs (size);

			r.width += Mathf.Abs (e.delta.x);
			if (e.delta.x < 0f) {
				r.x += e.delta.x;
			}

			if (r.Contains (e.mousePosition)) {
				EditorGUIUtility.AddCursorRect (new Rect (e.mousePosition, Vector2.one * 32f), MouseCursor.ResizeHorizontal);

				if (e.type == EventType.MouseDrag) {
					rect.x = Mathf.Clamp (rect.x + e.delta.x, min, max);
					e.Use ();
					return true;
				}
			}

			return false;
		}

		public static float MakeHorizontalDragRect (Rect rect, float min, float max, float size = 5f, Color? color = null) {
			Color col = color.HasValue ? color.Value : new Color (0.1f, 0.1f, 0.1f);

			EditorGUI.DrawRect (rect, col);
			Event e = Event.current;

			if (e.type == EventType.Used) {
				return rect.x;
			}

			Rect r = new Rect (rect);
			r.x -= size * 0.5f;
			r.width = Mathf.Abs (size);

			r.width += Mathf.Abs (e.delta.x);
			if (e.delta.x < 0f) {
				r.x += e.delta.x;
			}

			if (r.Contains (e.mousePosition)) {
				EditorGUIUtility.AddCursorRect (new Rect (e.mousePosition, Vector2.one * 32f), MouseCursor.ResizeHorizontal);

				if (e.type == EventType.MouseDrag) {
					rect.x = Mathf.Clamp (rect.x + e.delta.x, min, max);
					e.Use ();
				}
			}

			return rect.x;
		}

		private static SettingsAsset FindAsset () {
			string[] guids = AssetDatabase.FindAssets ($"t:{typeof (SettingsAsset).FullName}");
			if (guids.Length > 0) {
				return AssetDatabase.LoadAssetAtPath<SettingsAsset> (AssetDatabase.GUIDToAssetPath (guids[0]));
			}
			return null;
		}

		private void LoadViableTypes () {
			if (viableSettingTypes != null && viableGroupTypes != null) {
				return;
			}

			List<Type> settingTypes = new List<Type> ();
			Type settingBaseType = typeof (SettingBase);

			List<Type> groupTypes = new List<Type> ();
			Type groupBaseType = typeof (SettingsGroup);

			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies ();
			foreach (Assembly asm in assemblies) {

				Type[] asmTypes = asm.GetTypes ();
				foreach (Type t in asmTypes) {

					if (settingBaseType.IsAssignableFrom (t) && !t.IsAbstract) {
						settingTypes.Add (t);
					}
					if (groupBaseType.IsAssignableFrom (t) && !t.IsAbstract && t != groupBaseType && t != typeof (SettingsAsset)) {
						groupTypes.Add (t);
					}

				}
			}

			if (viableSettingTypes == null) {
				settingTypes.Sort (Compare);
				viableSettingTypes = settingTypes.ToArray ();
			}
			if (viableGroupTypes == null) {
				groupTypes.Sort (Compare);
				viableGroupTypes = groupTypes.ToArray ();
			}
		}

		private int Compare (Type a, Type b) {
			return a.FullName.CompareTo (b.FullName);
		}

		private Color GetHierarchyColor (ScriptableObject obj, int index, bool hovered) {
			if (obj == dragged) {
				return hierarchyColorDragged;
			} else if (obj == selected) {
				return hierarchyColorSelected;
			} else if (hovered) {
				return hierarchyColorHover;
			} else {
				return index % 2 == 0 ? hierarchyColorA : hierarchyColorB;
			}
		}

		private void ExpandToSelection (bool collapseOthers) {
			if (collapseOthers) {
				expansionState.Clear ();
			}
			SettingsGroup sel = selected is SettingsGroup g ? g : (selected is SettingBase s ? s.group : null);
			int i = 0;
			while (sel != null && i < 50) {
				expansionState[sel] = true;
				sel = sel.Parent;
				i++;
			}
		}

		private string ToEditorSpelling (string value) {
			if (value.StartsWith ("<")) {
				value = value.Substring (1);
				value = value.Replace (">k__BackingField", "");
			}

			if (value.Length > 2 && value[1] == '_') {
				value = value.Substring (2);
			}

			value = value.Replace ("_", " ");

			string val = "";
			for (int i = 0; i < value.Length; i++) {
				if (i == 0) {
					val = char.ToUpper (value[0]).ToString ();
					continue;
				}

				val = val + value[i];

				if (i < value.Length - 1) {
					char next = value[i + 1];
					if ((char.IsUpper (next) || char.IsNumber (next)) && !char.IsUpper (value[i]) && value[i] != '.') {
						val = val + " ";
					}
				}
			}

			return val.TrimEnd ('_');
		}

		private void LoadSettingsAssets () {
			List<SettingsAsset> assets = new List<SettingsAsset> (10);
			Type assetType = typeof (SettingsAsset);

			foreach (var guid in AssetDatabase.FindAssets ($"t:{assetType.FullName}")) {
				if (AssetDatabase.LoadAssetAtPath (AssetDatabase.GUIDToAssetPath (guid), assetType) is SettingsAsset a) {
					assets.Add (a);
				}
			}

			allAssets = assets.ToArray ();
		}

		// Hierarchy Serialization

		void ISerializationCallbackReceiver.OnBeforeSerialize () {
			hierarchyState = new List<SettingsGroup> ();
			foreach (var kvp in expansionState) {
				if (kvp.Value) {
					hierarchyState.Add (kvp.Key);
				}
			}
		}

		void ISerializationCallbackReceiver.OnAfterDeserialize () {
			if (hierarchyState != null) {
				foreach (var o in hierarchyState) {
					if (o != null) {
						expansionState[o] = true;
					}
				}
			}
		}

		private class NewSettingData {
			public readonly Type SettingType;
			public SettingsGroup Group;

			public NewSettingData (Type settingType, SettingsGroup group) {
				SettingType = settingType;
				Group = group;
			}
		}

	}
}