using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System;
using System.Text;
using System.Collections.Generic;
using MVR.FileManagementSecure;
using SimpleJSON;

namespace MacGruber
{
	public partial class IdlePoser : MVRScript
	{
		private List<UIDynamic> myMenuCached = new List<UIDynamic>();
		private List<object> myMenuElements = new List<object>();

		private int myMenuItem = 0;
		private JSONStorableUrl myDataFile;
		private JSONStorableUrl myLayerFile;
		private UIDynamicTabBar myMenuTabBar;
		private static JSONStorableStringChooser myMainAnimation;
		private static JSONStorableStringChooser myMainLayer;
		private static JSONStorableStringChooser myMainState;
		private JSONStorableString myGeneralInfo;
		private JSONStorableString myPlayInfo;
		private JSONStorableBool myPlayPaused;
		private JSONStorableStringChooser myCapturesControlList;
		private JSONStorableStringChooser myCapturesMorphList;
		private UIDynamicPopup myCapturesMorphPopup;
		private JSONStorableString myCapturesMorphFullname;
		private JSONStorableBool myStateAutoTransition;
		private JSONStorableStringChooser myAnchorCaptureList;
		private JSONStorableStringChooser myAnchorModeList;
		private JSONStorableStringChooser myAnchorAtomListA;
		private JSONStorableStringChooser myAnchorAtomListB;
		private JSONStorableStringChooser myAnchorControlListA;
		private JSONStorableStringChooser myAnchorControlListB;
		private JSONStorableFloat myAnchorBlendRatio;
		private JSONStorableFloat myAnchorDampingTime;
		private JSONStorableStringChooser myTransitionList;
		private JSONStorableBool myOptionsDefaultToWorldAnchor;
		private JSONStorableBool myDebugShowInfo;
		private JSONStorableBool myDebugShowPaths;
		private JSONStorableBool myDebugShowSelectedOnly;
		private JSONStorableBool myDebugShowTransitions;

		private bool myIsFullRefresh = true;

		private readonly List<string> myStateTypes = new List<string>() {
			"Regular State",
			COLORTAG_CONTROLPOINT+"Control Point</color>",
			COLORTAG_INTERMEDIATE+"Intermediate Point</color>"
		};
		private readonly List<string> myStateGroups = new List<string>() {
			"None",
			"Group A", "Group B", "Group C", "Group D",
			"Group E", "Group F", "Group G", "Group H",
			"Group I", "Group J", "Group K", "Group L"
		};
		private readonly List<string> myAnchorModes = new List<string>() {
			"World",
			"Single Anchor",
			"Blend Anchor",
			"Relative"
		};

		private const string FILE_EXTENSION = "idlepose";
		private const string BASE_DIRECTORY = "Saves/PluginData/IdlePoser";
		private const string COLORTAG_INTERMEDIATE = "<color=#804b00>";
		private const string COLORTAG_CONTROLPOINT = "<color=#005780>";

		private const string DEFAULT_MORPH_UID = "Left Thumb Bend";

		private const int MENU_PLAY        = 0;
		private const int MENU_CAPTURES    = 1;
		private const int MENU_STATES      = 2;
		private const int MENU_TRANSITIONS = 3;
		private const int MENU_TRIGGERS    = 4;
		private const int MENU_ANCHORS     = 5;
		private const int MENU_OPTIONS     = 6;

		private GameObject myLabelWith2BXButtonPrefab;
		private GameObject myLabelWithMXButtonPrefab;

		// =======================================================================================


		private void InitUI()
		{
			FileManagerSecure.CreateDirectory(BASE_DIRECTORY);
			
			myDataFile = new JSONStorableUrl("IdlePose", "", UILoadAnimationsJSON, FILE_EXTENSION, true);
			myLayerFile = new JSONStorableUrl("IdlePose", "", UILoadJSON, FILE_EXTENSION, true);

			List<string> animationItems = new List<string>();
			myMainAnimation = new JSONStorableStringChooser("Animation", animationItems, "", "Animation");
			myMainAnimation.setCallbackFunction += UISelectAnimationAndRefresh;

			List<string> layerItems = new List<string>();
			myMainLayer = new JSONStorableStringChooser("Layer", layerItems, "", "Layer");
			myMainLayer.setCallbackFunction += UISelectLayerAndRefresh;

			List<string> stateItems = new List<string>();
			myMainState = new JSONStorableStringChooser("State", stateItems, "", "State");
			myMainState.setCallbackFunction += UISelectStateAndRefresh;

			myGeneralInfo = new JSONStorableString("Info", "");
			myPlayInfo = new JSONStorableString("Info", "");
			myPlayPaused = new JSONStorableBool("Animation Paused", false, UISetPaused);

			pluginLabelJSON.setCallbackFunction += (string label) => {
				if (!string.IsNullOrEmpty(label))
					label = ": " + label;
				myGeneralInfo.val = "<color=#606060><size=40><b>IdlePoser"+label+"</b></size>\n" +
					"State-based character idle animation system.</color>\n\nThere is a tutorial available for this plugin, find it on the VaM Hub!";
			};
			pluginLabelJSON.setCallbackFunction(pluginLabelJSON.val);

			myCapturesControlList = new JSONStorableStringChooser("Control", new List<string>(), "", "Control");
			myCapturesMorphList = new JSONStorableStringChooser("Morphs", new List<string>(), "", "Morphs");
			myCapturesMorphFullname = new JSONStorableString("Fullname", "");
			myCapturesMorphList.setCallbackFunction += UIUpdateMorphFullename;

			myStateAutoTransition = new JSONStorableBool("Auto-Transition on State Change", true);
			myTransitionList = new JSONStorableStringChooser("Transition", new List<string>(), "", "Transition");
			myAnchorCaptureList = new JSONStorableStringChooser("ControlCapture", new List<string>(), "", "Control Capture");
			myAnchorCaptureList.setCallbackFunction += (string v) => UIRefreshMenu();

			myAnchorModeList = new JSONStorableStringChooser("AnchorMode", myAnchorModes, "", "Anchor Mode");
			myAnchorModeList.setCallbackFunction += (string v) => UISetAnchors();
			myAnchorAtomListA = new JSONStorableStringChooser("AnchorAtomA", new List<string>(), "", "Anchor Atom A");
			myAnchorAtomListA.setCallbackFunction += (string v) => UISetAnchors();
			myAnchorAtomListB = new JSONStorableStringChooser("AnchorAtomB", new List<string>(), "", "Anchor Atom B");
			myAnchorAtomListB.setCallbackFunction += (string v) => UISetAnchors();
			myAnchorControlListA = new JSONStorableStringChooser("AnchorControlA", new List<string>(), "", "Anchor Control A");
			myAnchorControlListA.setCallbackFunction += (string v) => UISetAnchors();
			myAnchorControlListB = new JSONStorableStringChooser("AnchorControlB", new List<string>(), "", "Anchor Control B");
			myAnchorControlListB.setCallbackFunction += (string v) => UISetAnchors();
			myAnchorBlendRatio = new JSONStorableFloat("Anchor Blend Ratio", DEFAULT_ANCHOR_BLEND_RATIO, 0.0f, 1.0f, true, true);
			myAnchorBlendRatio.setCallbackFunction += (float v) => UISetAnchorsBlendDamp();
			myAnchorDampingTime = new JSONStorableFloat("Anchor Damping Time", DEFAULT_ANCHOR_DAMPING_TIME, 0.0f, 5.0f, true, true);
			myAnchorDampingTime.setCallbackFunction += (float v) => UISetAnchorsBlendDamp();

			myOptionsDefaultToWorldAnchor = new JSONStorableBool("Default to World Anchor", false);
			myDebugShowInfo = new JSONStorableBool("Show Debug Info", false);
			myDebugShowInfo.setCallbackFunction += (bool v) => UIRefreshMenu();
			myDebugShowPaths = new JSONStorableBool("Draw Paths", false);
			myDebugShowPaths.setCallbackFunction += DebugSwitchShowCurves;
			myDebugShowTransitions = new JSONStorableBool("Draw Transitions", false);
			myDebugShowTransitions.setCallbackFunction += DebugSwitchShowCurves;
			myDebugShowSelectedOnly = new JSONStorableBool("Draw Selected Only", false);

			UIRefreshMenu();
		}

		private void OnDestroyUI()
		{
			Utils.OnDestroyUI();

			if (myLabelWith2BXButtonPrefab != null)
				Destroy(myLabelWith2BXButtonPrefab);
			if (myLabelWithMXButtonPrefab != null)
				Destroy(myLabelWithMXButtonPrefab);
			myLabelWith2BXButtonPrefab = null;
			myLabelWithMXButtonPrefab = null;

			DestroyDebugCurves();
		}

		private void InitPrefabs()
		{
			{
				myLabelWith2BXButtonPrefab = new GameObject("Label2BXButton");
				myLabelWith2BXButtonPrefab.SetActive(false);
				RectTransform rt = myLabelWith2BXButtonPrefab.AddComponent<RectTransform>();
				rt.anchorMax = new Vector2(0, 1);
				rt.anchorMin = new Vector2(0, 1);
				rt.offsetMax = new Vector2(535, -500);
				rt.offsetMin = new Vector2(10, -600);
				LayoutElement le = myLabelWith2BXButtonPrefab.AddComponent<LayoutElement>();
				le.flexibleWidth = 1;
				le.minHeight = 50;
				le.minWidth = 350;
				le.preferredHeight = 50;
				le.preferredWidth = 500;

				RectTransform backgroundTransform = manager.configurableScrollablePopupPrefab.transform.Find("Background") as RectTransform;
				backgroundTransform = Instantiate(backgroundTransform, myLabelWith2BXButtonPrefab.transform);
				backgroundTransform.name = "Background";
				backgroundTransform.anchorMax = new Vector2(1, 1);
				backgroundTransform.anchorMin = new Vector2(0, 0);
				backgroundTransform.offsetMax = new Vector2(0, 0);
				backgroundTransform.offsetMin = new Vector2(0, -10);

				RectTransform xButtonTransform = manager.configurableScrollablePopupPrefab.transform.Find("Button") as RectTransform;
				xButtonTransform = Instantiate(xButtonTransform, myLabelWith2BXButtonPrefab.transform);
				xButtonTransform.name = "ButtonX";
				xButtonTransform.anchorMax = new Vector2(1, 1);
				xButtonTransform.anchorMin = new Vector2(1, 0);
				xButtonTransform.offsetMax = new Vector2(0, 0);
				xButtonTransform.offsetMin = new Vector2(-60, -10);
				Button buttonX = xButtonTransform.GetComponent<Button>();
				Text xButtonText = xButtonTransform.Find("Text").GetComponent<Text>();
				xButtonText.text = "X";
				Image xButtonImage = xButtonTransform.GetComponent<Image>();

				RectTransform labelTransform = xButtonText.rectTransform;
				labelTransform = Instantiate(labelTransform, myLabelWith2BXButtonPrefab.transform);
				labelTransform.name = "Text";
				labelTransform.anchorMax = new Vector2(1, 1);
				labelTransform.anchorMin = new Vector2(0, 0);
				labelTransform.offsetMax = new Vector2(-65, 0);
				labelTransform.offsetMin = new Vector2(100, -10);
				Text labelText = labelTransform.GetComponent<Text>();
				labelText.verticalOverflow = VerticalWrapMode.Overflow;

				RectTransform toggleBG1Transform = manager.configurableTogglePrefab.transform.Find("Panel") as RectTransform;
				toggleBG1Transform = Instantiate(toggleBG1Transform, myLabelWith2BXButtonPrefab.transform);
				toggleBG1Transform.name = "ToggleBG1";
				toggleBG1Transform.anchorMax = new Vector2(0, 1);
				toggleBG1Transform.anchorMin = new Vector2(0, 0);
				toggleBG1Transform.offsetMax = new Vector2(50, 0);
				toggleBG1Transform.offsetMin = new Vector2(0, -10);
				Image toggleBG1Image = toggleBG1Transform.GetComponent<Image>();
				toggleBG1Image.sprite = xButtonImage.sprite;
				toggleBG1Image.color = xButtonImage.color;
				Toggle toggle1 = toggleBG1Transform.gameObject.AddComponent<Toggle>();
				toggle1.isOn = true;

				RectTransform toggle1Check = manager.configurableTogglePrefab.transform.Find("Background/Checkmark") as RectTransform;
				toggle1Check = Instantiate(toggle1Check, toggleBG1Transform);
				toggle1Check.name = "Toggle1";
				toggle1Check.anchorMax = new Vector2(1, 1);
				toggle1Check.anchorMin = new Vector2(0, 0);
				toggle1Check.offsetMax = new Vector2(2, -10);
				toggle1Check.offsetMin = new Vector2(3, -10);
				Image image1 = toggle1Check.GetComponent<Image>();

				RectTransform toggle1Label = Instantiate(xButtonText.rectTransform, toggle1Check);
				toggle1Label.name = "Toggle1Label";
				toggle1Label.anchorMax = new Vector2(1.0f, 1.0f);
				toggle1Label.anchorMin = new Vector2(0.0f, 0.5f);
				toggle1Label.offsetMax = new Vector2(0, 4);
				toggle1Label.offsetMin = new Vector2(-4, 4);
				Text toggle1Text = toggle1Label.GetComponent<Text>();
				toggle1Text.fontSize = 20;
				toggle1Text.text = "POS";
				toggle1Text.alignment = TextAnchor.UpperCenter;

				RectTransform toggleBG2Transform = manager.configurableTogglePrefab.transform.Find("Panel") as RectTransform;
				toggleBG2Transform = Instantiate(toggleBG2Transform, myLabelWith2BXButtonPrefab.transform);
				toggleBG2Transform.name = "ToggleBG2";
				toggleBG2Transform.anchorMax = new Vector2(0, 1);
				toggleBG2Transform.anchorMin = new Vector2(0, 0);
				toggleBG2Transform.offsetMax = new Vector2(100, 0);
				toggleBG2Transform.offsetMin = new Vector2(50, -10);
				Image toggleBG2Image = toggleBG2Transform.GetComponent<Image>();
				toggleBG2Image.sprite = xButtonImage.sprite;
				toggleBG2Image.color = xButtonImage.color;
				Toggle toggle2 = toggleBG2Transform.gameObject.AddComponent<Toggle>();
				toggle2.isOn = true;

				RectTransform toggle2Check = manager.configurableTogglePrefab.transform.Find("Background/Checkmark") as RectTransform;
				toggle2Check = Instantiate(toggle2Check, toggleBG2Transform);
				toggle2Check.name = "Toggle2";
				toggle2Check.anchorMax = new Vector2(1, 1);
				toggle2Check.anchorMin = new Vector2(0, 0);
				toggle2Check.offsetMax = new Vector2(2, -10);
				toggle2Check.offsetMin = new Vector2(3, -10);
				Image image2 = toggle2Check.GetComponent<Image>();

				RectTransform toggle2Label = Instantiate(xButtonText.rectTransform, toggle2Check);
				toggle2Label.name = "Toggle2Label";
				toggle2Label.anchorMax = new Vector2(1.0f, 1.0f);
				toggle2Label.anchorMin = new Vector2(0.0f, 0.5f);
				toggle2Label.offsetMax = new Vector2(0, 4);
				toggle2Label.offsetMin = new Vector2(-4, 4);
				Text toggle2Text = toggle2Label.GetComponent<Text>();
				toggle2Text.fontSize = 20;
				toggle2Text.text = "ROT";
				toggle2Text.alignment = TextAnchor.UpperCenter;

				UIDynamicLabel2BXButton uid = myLabelWith2BXButtonPrefab.AddComponent<UIDynamicLabel2BXButton>();
				uid.label = labelText;
				uid.image1 = image1;
				uid.image2 = image2;
				uid.toggle1 = toggle1;
				uid.toggle2 = toggle2;
				uid.text1 = toggle1Text;
				uid.text2 = toggle2Text;
				uid.buttonX = buttonX;
			}

			{
				myLabelWithMXButtonPrefab = new GameObject("LabelMXButton");
				myLabelWithMXButtonPrefab.SetActive(false);
				RectTransform rt = myLabelWithMXButtonPrefab.AddComponent<RectTransform>();
				rt.anchorMax = new Vector2(0, 1);
				rt.anchorMin = new Vector2(0, 1);
				rt.offsetMax = new Vector2(535, -500);
				rt.offsetMin = new Vector2(10, -600);
				LayoutElement le = myLabelWithMXButtonPrefab.AddComponent<LayoutElement>();
				le.flexibleWidth = 1;
				le.minHeight = 50;
				le.minWidth = 350;
				le.preferredHeight = 50;
				le.preferredWidth = 500;

				RectTransform backgroundTransform = manager.configurableScrollablePopupPrefab.transform.Find("Background") as RectTransform;
				backgroundTransform = Instantiate(backgroundTransform, myLabelWithMXButtonPrefab.transform);
				backgroundTransform.name = "Background";
				backgroundTransform.anchorMax = new Vector2(1, 1);
				backgroundTransform.anchorMin = new Vector2(0, 0);
				backgroundTransform.offsetMax = new Vector2(0, 0);
				backgroundTransform.offsetMin = new Vector2(0, -10);

				RectTransform xButtonTransform = manager.configurableScrollablePopupPrefab.transform.Find("Button") as RectTransform;
				xButtonTransform = Instantiate(xButtonTransform, myLabelWithMXButtonPrefab.transform);
				xButtonTransform.name = "ButtonX";
				xButtonTransform.anchorMax = new Vector2(1, 1);
				xButtonTransform.anchorMin = new Vector2(1, 0);
				xButtonTransform.offsetMax = new Vector2(0, 0);
				xButtonTransform.offsetMin = new Vector2(-60, -10);
				Button buttonX = xButtonTransform.GetComponent<Button>();
				Text xButtonText = xButtonTransform.Find("Text").GetComponent<Text>();
				xButtonText.text = "X";
				Image xButtonImage = xButtonTransform.GetComponent<Image>();

				RectTransform labelTransform = xButtonText.rectTransform;
				labelTransform = Instantiate(labelTransform, myLabelWithMXButtonPrefab.transform);
				labelTransform.name = "Text";
				labelTransform.anchorMax = new Vector2(1, 1);
				labelTransform.anchorMin = new Vector2(0, 0);
				labelTransform.offsetMax = new Vector2(-65, 0);
				labelTransform.offsetMin = new Vector2(50, -10);
				Text labelText = labelTransform.GetComponent<Text>();
				labelText.verticalOverflow = VerticalWrapMode.Overflow;

				RectTransform toggleBGMTransform = manager.configurableTogglePrefab.transform.Find("Panel") as RectTransform;
				toggleBGMTransform = Instantiate(toggleBGMTransform, myLabelWithMXButtonPrefab.transform);
				toggleBGMTransform.name = "ToggleBGM";
				toggleBGMTransform.anchorMax = new Vector2(0, 1);
				toggleBGMTransform.anchorMin = new Vector2(0, 0);
				toggleBGMTransform.offsetMax = new Vector2(50, 0);
				toggleBGMTransform.offsetMin = new Vector2(0, -10);
				Image toggleBGMImage = toggleBGMTransform.GetComponent<Image>();
				toggleBGMImage.sprite = xButtonImage.sprite;
				toggleBGMImage.color = xButtonImage.color;
				Toggle toggleM = toggleBGMTransform.gameObject.AddComponent<Toggle>();
				toggleM.isOn = true;

				RectTransform toggleMCheck = manager.configurableTogglePrefab.transform.Find("Background/Checkmark") as RectTransform;
				toggleMCheck = Instantiate(toggleMCheck, toggleBGMTransform);
				toggleMCheck.name = "ToggleM";
				toggleMCheck.anchorMax = new Vector2(1, 1);
				toggleMCheck.anchorMin = new Vector2(0, 0);
				toggleMCheck.offsetMax = new Vector2(2, -5);
				toggleMCheck.offsetMin = new Vector2(3, -5);
				Image imageM = toggleMCheck.GetComponent<Image>();

				RectTransform toggleMLabel = Instantiate(xButtonText.rectTransform, toggleMCheck);
				toggleMLabel.name = "ToggleMLabel";
				toggleMLabel.anchorMax = new Vector2(0.5f, 1.0f);
				toggleMLabel.anchorMin = new Vector2(0.0f, 0.5f);
				toggleMLabel.offsetMax = new Vector2(0, 0);
				toggleMLabel.offsetMin = new Vector2(0, 0);
				Text toggleMText = toggleMLabel.GetComponent<Text>();
				toggleMText.fontSize = 22;
				toggleMText.text = "M";

				UIDynamicLabelMXButton uid = myLabelWithMXButtonPrefab.AddComponent<UIDynamicLabelMXButton>();
				uid.label = labelText;
				uid.imageM = imageM;
				uid.toggleM = toggleM;
				uid.buttonX = buttonX;
			}
		}

		private void UIRefreshMenu()
		{
			CleanupMenu();
			CreateMainUI();
			// myIsFullRefresh = false;
			UISelectMenu(myMenuItem);
			myIsFullRefresh = true;
		}

		private void UISelectMenu(int menuItem)
		{
			myMenuItem = menuItem;

			for (int i=0; i<myMenuTabBar.buttons.Count; ++i)
				myMenuTabBar.buttons[i].interactable = (i != myMenuItem);

			myPaused = (myMenuItem != MENU_PLAY || myPlayPaused.val);
			myPlayMode = (myMenuItem == MENU_PLAY);
			myMainAnimation.popup.visible = true; // Workaround for PopupPanel appearing behind other UI for some reason
			myMainAnimation.popup.visible = false;
			myMainLayer.popup.visible = true; // Workaround for PopupPanel appearing behind other UI for some reason
			myMainLayer.popup.visible = false;
			myMainState.popup.visible = true; // Workaround for PopupPanel appearing behind other UI for some reason
			myMainState.popup.visible = false;
			// CleanupMenu();

			List<string> animations = new List<string>();
			foreach (var animation in myAnimations)
				animations.Add(animation.Key);
			animations.Sort();
			myMainAnimation.choices = animations;
			myMainAnimation.displayChoices = animations;
			if (animations.Count == 0)
			{
				myMainAnimation.valNoCallback = "";
			}
			else if (!animations.Contains(myMainAnimation.val))
			{
				myMainAnimation.valNoCallback = animations[0];
				Animation animation;
				myAnimations.TryGetValue(myMainAnimation.val, out animation);
				SetAnimation(animation);
				UIBlendToState();
			}

			List<string> layers = new List<string>();
			if(myCurrentAnimation != null)
			{
				foreach (var layer in myCurrentAnimation.myLayers)
					layers.Add(layer.Key);
			}

			layers.Sort();
			myMainLayer.choices = layers;
			myMainLayer.displayChoices = layers;
			if (layers.Count == 0)
			{
				myMainLayer.valNoCallback = "";
			}
			else if (!layers.Contains(myMainLayer.val))
			{
				myMainLayer.valNoCallback = layers[0];
				Layer layer;
				myCurrentAnimation.myLayers.TryGetValue(myMainLayer.val, out layer);
				SetLayer(layer);
				UIBlendToState();
			}

			List<string> states = new List<string>();
			if(myCurrentLayer != null)
			{
				foreach (var state in myCurrentLayer.myStates)
					states.Add(state.Key);
			}
			states.Sort();
			List<string> stateDisplays = new List<string>(states.Count);
			for (int i=0; i<states.Count; ++i)
			{
				State state = myCurrentLayer.myStates[states[i]];
				if (state.IsIntermediate)
					stateDisplays.Add(COLORTAG_INTERMEDIATE+state.myName+"</color>");
				else if (state.IsControlPoint)
					stateDisplays.Add(COLORTAG_CONTROLPOINT+state.myName+"</color>");
				else
					stateDisplays.Add(state.myName);
			}
			myMainState.choices = states;
			myMainState.displayChoices = stateDisplays;
			if (states.Count == 0)
			{
				myMainState.valNoCallback = "";
			}
			else if (!states.Contains(myMainState.val))
			{
				myMainState.valNoCallback = states[0];
				UIBlendToState();
			}

			Utils.OnInitUI(CreateUIElement);
			switch (myMenuItem)
			{
				case MENU_CAPTURES:    CreateCapturesMenu();    break;
				case MENU_STATES:      CreateStateMenu();       break;
				case MENU_TRANSITIONS: CreateTransitionsMenu(); break;
				case MENU_TRIGGERS:    CreateTriggers();        break;
				case MENU_ANCHORS:     CreateAnchorsMenu();     break;
				case MENU_OPTIONS:     CreateOptionsMenu();     break;
				default:               CreatePlayMenu();        break;
			}
		}

		private void UISetPaused(bool paused)
		{
			if (myMenuItem == MENU_PLAY)
				myPaused = paused;
		}

		private void UIBlendToState()
		{
			State state;
			if (myStateAutoTransition.val && myCurrentLayer.myStates.TryGetValue(myMainState.val, out state))
				myCurrentLayer.SetBlendTransition(state, true);
		}

		private void UISelectAnimationAndRefresh(string name)
		{
			Animation animation;
			myAnimations.TryGetValue(myMainAnimation.val, out animation);
			SetAnimation(animation);

			List<string> layers = myCurrentAnimation.myLayers.Keys.ToList();
			layers.Sort();
			if(layers.Count > 0) {
				Layer layer;
				myCurrentAnimation.myLayers.TryGetValue(layers[0], out layer);
				SetLayer(layer);

				List<string> states = layer.myStates.Keys.ToList();
				states.Sort();
				if(states.Count > 0) {
					State state;
					layer.myStates.TryGetValue(states[0], out state);
					layer.SetBlendTransition(state);
				}
			}
			UIRefreshMenu();
		}
		private void UISelectLayerAndRefresh(string name)
		{
			Layer layer;
			myCurrentAnimation.myLayers.TryGetValue(myMainLayer.val, out layer);
			SetLayer(layer);

			List<string> states = layer.myStates.Keys.ToList();
			states.Sort();
			if(states.Count > 0) {
				State state;
				layer.myStates.TryGetValue(states[0], out state);
				layer.SetBlendTransition(state);
			}

			UIRefreshMenu();
		}

		private void UISelectStateAndRefresh(string name)
		{
			UIBlendToState();
			UIRefreshMenu();
		}

		private void CreateMainUI()
		{
			Utils.OnInitUI(CreateUIElement);
			CreateMenuInfo(myGeneralInfo, 229, true);

			{
				UIDynamicButton button = CreateButton("Load", false);
				myDataFile.setCallbackFunction -= UILoadAnimationsJSON;
				myDataFile.allowFullComputerBrowse = false;
				myDataFile.allowBrowseAboveSuggestedPath = true;
				myDataFile.SetFilePath(BASE_DIRECTORY+"/");
				myDataFile.RegisterFileBrowseButton(button.button);
				myDataFile.setCallbackFunction += UILoadAnimationsJSON;
				myMenuElements.Add(button);

				CreateMenuButton("Save As", UISaveAnimationsJSONDialog, false);
			}

			{
				UIDynamicButton button = CreateButton("Load Layer", false);
				myLayerFile.setCallbackFunction -= UILoadJSON;
				myLayerFile.allowFullComputerBrowse = false;
				myLayerFile.allowBrowseAboveSuggestedPath = true;
				myLayerFile.SetFilePath(BASE_DIRECTORY+"/");
				myLayerFile.RegisterFileBrowseButton(button.button);
				myLayerFile.setCallbackFunction += UILoadJSON;
				myMenuElements.Add(button);

				CreateMenuButton("Save Layer As", UISaveJSONDialog, false);
			}

			CreateMenuPopup(myMainAnimation, false);
			CreateMenuPopup(myMainLayer, false);
			CreateMenuPopup(myMainState, false);
			CreateAnimationsMenu();
			CreateLayersMenu();

			{
				GameObject tabbarPrefab = new GameObject("TabBar");
				RectTransform rt = tabbarPrefab.AddComponent<RectTransform>();
				rt.anchorMax = new Vector2(0, 1);
				rt.anchorMin = new Vector2(0, 1);
				rt.offsetMax = new Vector2(1064, -500);
				rt.offsetMin = new Vector2(10, -600);
				LayoutElement le = tabbarPrefab.AddComponent<LayoutElement>();
				le.flexibleWidth = 1;
				le.minHeight = 90;
				le.minWidth = 1064;
				le.preferredHeight = 90;
				le.preferredWidth = 1064;

				RectTransform backgroundTransform = manager.configurableScrollablePopupPrefab.transform.Find("Background") as RectTransform;
				backgroundTransform = Instantiate(backgroundTransform, tabbarPrefab.transform);
				backgroundTransform.name = "Background";
				backgroundTransform.anchorMax = new Vector2(1, 1);
				backgroundTransform.anchorMin = new Vector2(0, 0);
				backgroundTransform.offsetMax = new Vector2(0, 0);
				backgroundTransform.offsetMin = new Vector2(0, 0);

				UIDynamicTabBar uid = tabbarPrefab.AddComponent<UIDynamicTabBar>();

				string[] menuItems = new string[] { "Play", "Captures", "States", "Transitions", "Triggers", "Anchors", "Options" };
				float width = 142.0f;
				float padding = 5.0f;
				float x = 15;
				for (int i=0; i<menuItems.Length; ++i)
				{
					float extraWidth = i == MENU_TRANSITIONS ? 11.0f : 0.0f;

					RectTransform buttonTransform = manager.configurableScrollablePopupPrefab.transform.Find("Button") as RectTransform;
					buttonTransform = Instantiate(buttonTransform, tabbarPrefab.transform);
					buttonTransform.name = "Button";
					buttonTransform.anchorMax = new Vector2(0, 1);
					buttonTransform.anchorMin = new Vector2(0, 0);
					buttonTransform.offsetMax = new Vector2(x+width+extraWidth, -15);
					buttonTransform.offsetMin = new Vector2(x, 15);
					Button buttonButton = buttonTransform.GetComponent<Button>();
					uid.buttons.Add(buttonButton);
					Text buttonText = buttonTransform.Find("Text").GetComponent<Text>();
					buttonText.text = menuItems[i];
					x += width + extraWidth + padding;
				}

				Transform t = CreateUIElement(tabbarPrefab.transform, false);
				myMenuTabBar = t.gameObject.GetComponent<UIDynamicTabBar>();
				myMenuElements.Add(myMenuTabBar);
				for (int i=0; i<myMenuTabBar.buttons.Count; ++i)
				{
					int menuItem = i;
					myMenuTabBar.buttons[i].onClick.AddListener(
						() => {myMenuItem = menuItem; UIRefreshMenu();} //UISelectMenu(menuItem)
					);
				}

				Destroy(tabbarPrefab);
			}

			CreateMenuSpacer(780, true);

			// myMenuElements.Clear();
		}

		private void CreatePlayMenu()
		{
			CreateMenuInfoOneLine("<size=30><b>Play Idle</b></size>", false);

			if (!myDebugShowInfo.val)
			{
				if (myCurrentLayer != null && myCurrentLayer.myStates.Count > 0)
					myPlayInfo.val = "IdlePoser is playing animations.";
				else
					myPlayInfo.val = "You need to add some states and transitions before you can play animations.";
			}

			CreateMenuInfo(myPlayInfo, 300, false);
			CreateMenuToggle(myPlayPaused, false);
		}

		private void CreateCapturesMenu()
		{
			// control captures
			CreateMenuInfoOneLine("<size=30><b>Control Captures</b></size>", false);

			FreeControllerV3[] atomControls = GetAllControllers();
			List<string> availableControls = new List<string>();
			for (int i=0; i<atomControls.Length; ++i)
			{
				string control = atomControls[i].name;
				if (control.StartsWith("hair"))
					continue;
				if (myCurrentLayer.myControlCaptures.FindIndex(cc => cc.myName == control) >= 0)
					continue;
				availableControls.Add(control);
			}
			myCapturesControlList.choices = availableControls;
			if (availableControls.Count == 0)
				myCapturesControlList.val = "";
			else if (!availableControls.Contains(myCapturesControlList.val))
				myCapturesControlList.val = availableControls[0];

			if (availableControls.Count > 0)
			{
				CreateMenuPopup(myCapturesControlList, false);
				CreateMenuButton("Add Controller", UIAddControlCapture, false);
			}

			if (myCurrentLayer.myControlCaptures.Count > 0)
			{
				CreateMenuTwinButton(
					"Toggle Positions", UIToggleControlCapturePosition,
					"Toggle Rotations", UIToggleControlCaptureRotation, false);
				CreateMenuSpacer(15, false);
			}

			for (int i=0; i<myCurrentLayer.myControlCaptures.Count; ++i)
			{
				ControlCapture cc = myCurrentLayer.myControlCaptures[i];
				CreateMenuLabel2BXButton(
					cc.myName, "POS", "ROT", cc.myApplyPosition, cc.myApplyRotation,
					(bool v) => { cc.myApplyPosition = v; },
					(bool v) => { cc.myApplyRotation = v; },
					() => UIRemoveControlCapture(cc), false
				);
			}

			// morph captures
			CreateMenuInfoOneLine("<size=30><b>Morph Captures</b></size>", true);

			DAZCharacterSelector geometry = containingAtom.GetStorableByID("geometry") as DAZCharacterSelector;
			if (geometry != null)
			{
				if (myIsFullRefresh)
				{
					List<string> morphUIDs = new List<string>();
					bool isMale = geometry.gender == DAZCharacterSelector.Gender.Male;
					if (!isMale || geometry.GetBoolParamValue("useFemaleMorphsOnMale"))
						UICollectMorphsUIDs(DAZCharacterSelector.Gender.Female, geometry, morphUIDs);
					if (isMale || geometry.GetBoolParamValue("useMaleMorphsOnFemale"))
						UICollectMorphsUIDs(DAZCharacterSelector.Gender.Male, geometry, morphUIDs);
					morphUIDs.Sort();
					List<string> morphNames = new List<string>();
					UICollectMorphsNames(geometry, morphUIDs, morphNames);

					myCapturesMorphList.choices = morphUIDs;
					myCapturesMorphList.displayChoices = morphNames;
					if (morphUIDs.Count == 0)
					{
						myCapturesMorphList.val = "";
					}
					else if (!morphUIDs.Contains(myCapturesMorphList.val))
					{
						string defaultMorph = DEFAULT_MORPH_UID + "#" + geometry.gender.ToString();
						myCapturesMorphList.val = morphUIDs.Contains(defaultMorph) ? defaultMorph : morphUIDs[0];
					}
				}

				CreateMenuFilterPopupCached(myCapturesMorphList, true, ref myCapturesMorphPopup);
				CreateMenuInfo(myCapturesMorphFullname, 50, true);

				CreateMenuButton("Add Morph", UIAddMorphCapture, true);
			}

			if (myCurrentLayer.myMorphCaptures.Count > 0)
			{
				CreateMenuButton("Toggle Morphs", UIToggleMorphCapture, true);
				CreateMenuSpacer(15, true);
			}

			for (int i=0; i<myCurrentLayer.myMorphCaptures.Count; ++i)
			{
				MorphCapture mc = myCurrentLayer.myMorphCaptures[i];
				DAZMorph m = mc.myMorph;
				string gender = mc.myGender.ToString().Substring(0,1);
				string label;
				if (m.hasBoneModificationFormulas) // this is a "high cost" morph...red warning
					label = "<color=#CC6060>"+gender+": " + m.resolvedRegionName + "</color><color=#FF0000>\n" + m.resolvedDisplayName + "</color>";
				else
					label = "<color=#606060>"+gender+": " + m.resolvedRegionName + "</color>\n" + m.resolvedDisplayName;
				CreateMenuLabelMXButton(label, mc.myApply, (bool v) => { mc.myApply = v; },	() => UIRemoveMorphCapture(mc), true);
			}
		}

		private void CreateStateMenu()
		{
			CreateMenuInfoOneLine("<size=30><b>State Manager</b></size>", false);
			CreateMenuSpacer(35, true);
			CreateMenuButton("Add State", UIAddState, false);
			State state;
			if (!myCurrentLayer.myStates.TryGetValue(myMainState.val, out state))
				return;

			CreateMenuButton("Remove State", UIRemoveState, false);
			CreateMenuToggle(myStateAutoTransition, false);

			CreateMenuButton("Capture State", UICaptureState, true);
			CreateMenuButton("Apply Anchors", UIApplyAnchors, true);

			CreateMenuSpacer(15, false);

			CreateMenuInfoOneLine("<size=30><b>State Settings</b></size>", false);
			CreateMenuSpacer(132, true);
			JSONStorableString name = new JSONStorableString("State Name", state.myName, UIRenameState);
			CreateMenuTextInput("Name", name, false);



			JSONStorableStringChooser typeChooser = new JSONStorableStringChooser("StateType", myStateTypes, myStateTypes[state.myStateType], "State\nType");
			typeChooser.setCallbackFunction += (string v) => {
				State s = UIGetState();
				if (s != null)
					s.myStateType = myStateTypes.IndexOf(v);
				UIRefreshMenu();
			};
			CreateMenuPopup(typeChooser, false);

			if (state.IsRegularState)
			{
				JSONStorableStringChooser groupChooser = new JSONStorableStringChooser("StateGroup", myStateGroups, myStateGroups[state.myStateGroup], "State\nGroup");
				groupChooser.setCallbackFunction += (string v) => {
					State s = UIGetState();
					if (s != null)
						s.myStateGroup = myStateGroups.IndexOf(v);
				};
				CreateMenuPopup(groupChooser, false);

				JSONStorableFloat probability = new JSONStorableFloat("Relative Probability", DEFAULT_PROBABILITY, 0.0f, 1.0f, true, true);
				probability.valNoCallback = state.myProbability;
				probability.setCallbackFunction = (float v) => {
					State s = UIGetState();
					if (s != null)
						s.myProbability = v;
				};
				CreateMenuSlider(probability, false);
			}
			
			JSONStorableBool allowInnerGroupTransition = new JSONStorableBool("Allow for in-group Transition", state.myAllowInGroupTransition);
			allowInnerGroupTransition.setCallbackFunction = (bool v) => {
				State s = UIGetState();
				if (s != null)
					s.myAllowInGroupTransition = v;
			};
			CreateMenuToggle(allowInnerGroupTransition, false);

			if (state.IsRegularState || state.IsIntermediate)
			{
				JSONStorableBool waitInfiniteDuration = new JSONStorableBool("Wait Infinite Duration", state.myWaitInfiniteDuration);
				waitInfiniteDuration.setCallbackFunction = (bool v) => {
					State s = UIGetState();
					if (s != null)
					{
						s.myWaitInfiniteDuration = v;
						myCurrentLayer.myDuration = v ? float.MaxValue : UnityEngine.Random.Range(s.myWaitDurationMin, s.myWaitDurationMax);
					}
					UIRefreshMenu();
				};
				CreateMenuToggle(waitInfiniteDuration, true);

				if (!state.myWaitInfiniteDuration)
				{
					JSONStorableBool waitForSync = new JSONStorableBool("Wait for TriggerSync", state.myWaitForSync);
					waitForSync.setCallbackFunction = (bool v) => {
						State s = UIGetState();
						if (s != null)
							s.myWaitForSync = v;
					};
					CreateMenuToggle(waitForSync, true);

					JSONStorableFloat waitDurationMin = new JSONStorableFloat("Wait Duration Min", DEFAULT_WAIT_DURATION_MIN, 0.0f, 300.0f, true, true);
					waitDurationMin.valNoCallback = state.myWaitDurationMin;
					waitDurationMin.setCallbackFunction = (float v) => {
						State s = UIGetState();
						if (s != null)
							s.myWaitDurationMin = v;
					};
					CreateMenuSlider(waitDurationMin, true);

					JSONStorableFloat waitDurationMax = new JSONStorableFloat("Wait Duration Max", DEFAULT_WAIT_DURATION_MAX, 0.0f, 300.0f, true, true);
					waitDurationMax.valNoCallback = state.myWaitDurationMax;
					waitDurationMax.setCallbackFunction = (float v) => {
						State s = UIGetState();
						if (s != null)
							s.myWaitDurationMax = v;
					};
					CreateMenuSlider(waitDurationMax, true);
				}
			}			
		}

		private void CreateAnchorsMenu()
		{
			CreateMenuInfoOneLine("<size=30><b>Anchors</b></size>", false);

			State state;
			if (!myCurrentLayer.myStates.TryGetValue(myMainState.val, out state))
			{
				CreateMenuInfo("You need to add a state before you can configure anchors.", 100, false);
				return;
			}

			List<string> captures = new List<string>(myCurrentLayer.myControlCaptures.Count);
			for (int i=0; i<myCurrentLayer.myControlCaptures.Count; ++i)
				captures.Add(myCurrentLayer.myControlCaptures[i].myName);
			myAnchorCaptureList.choices = captures;
			if (captures.Count == 0)
				myAnchorCaptureList.valNoCallback = "";
			else if (!captures.Contains(myAnchorCaptureList.val))
				myAnchorCaptureList.valNoCallback = captures[0];
			CreateMenuPopup(myAnchorCaptureList, false);

			ControlCapture controlCapture = myCurrentLayer.myControlCaptures.Find(cc => cc.myName == myAnchorCaptureList.val);
			if (controlCapture == null)
				return;

			CreateMenuSpacer(30, false);

			ControlEntryAnchored controlEntry;
			if (!state.myControlEntries.TryGetValue(controlCapture, out controlEntry))
				return;
			myAnchorModeList.valNoCallback = myAnchorModes[controlEntry.myAnchorMode];
			CreateMenuPopup(myAnchorModeList, false);

			if (controlEntry.myAnchorMode != ControlEntryAnchored.ANCHORMODE_WORLD)
			{
				bool isBlendMode = controlEntry.myAnchorMode == ControlEntryAnchored.ANCHORMODE_BLEND;

				List<string> atoms = new List<string>(GetAllAtomUIDs());
				atoms.Sort();
				myAnchorAtomListA.label = isBlendMode ? "Anchor Atom A" : "Anchor Atom";
				myAnchorAtomListA.valNoCallback = controlEntry.myAnchorAAtom;
				myAnchorAtomListA.choices = atoms;
				CreateMenuFilterPopup(myAnchorAtomListA, false);

				Atom atomA = GetAtomById(myAnchorAtomListA.val);
				List<string> controls = atomA?.GetStorableIDs() ?? new List<string>();
				controls.RemoveAll((string control) => {
					if (atomA == containingAtom && control == myAnchorCaptureList.val)
						return true;
					JSONStorable storable = atomA.GetStorableByID(control);
					if (storable is DAZBone || storable is FreeControllerV3)
						return false;
					return true;
				});
				myAnchorControlListA.label = isBlendMode ? "Anchor Control A" : "Anchor Control";
				myAnchorControlListA.valNoCallback = controlEntry.myAnchorAControl;
				myAnchorControlListA.choices = controls;
				if (controls.Count == 0)
					myAnchorControlListA.val = "";
				else if (!controls.Contains(myAnchorControlListA.val))
					myAnchorControlListA.val = controls[0];
				CreateMenuFilterPopup(myAnchorControlListA, false);

				if (isBlendMode)
				{
					myAnchorAtomListB.valNoCallback = controlEntry.myAnchorBAtom;
					myAnchorAtomListB.choices = atoms;
					CreateMenuFilterPopup(myAnchorAtomListB, false);

					Atom atomB = GetAtomById(myAnchorAtomListB.val);
					controls = atomB?.GetStorableIDs() ?? new List<string>();
					controls.RemoveAll((string control) => {
						if (atomB == containingAtom && control == myAnchorCaptureList.val)
							return true;
						JSONStorable storable = atomB.GetStorableByID(control);
						if (storable is DAZBone || storable is FreeControllerV3)
							return false;
						return true;
					});
					myAnchorControlListB.valNoCallback = controlEntry.myAnchorBControl;
					myAnchorControlListB.choices = controls;
					if (controls.Count == 0)
						myAnchorControlListB.val = "";
					else if (!controls.Contains(myAnchorControlListB.val))
						myAnchorControlListB.val = controls[0];
					CreateMenuFilterPopup(myAnchorControlListB, false);

					myAnchorBlendRatio.valNoCallback = controlEntry.myBlendRatio;
					CreateMenuSlider(myAnchorBlendRatio, false);
				}

				myAnchorDampingTime.valNoCallback = controlEntry.myDampingTime;
				CreateMenuSlider(myAnchorDampingTime, false);
			}
		}

		private void CreateTransitionsMenu()
		{
			CreateMenuInfoOneLine("<size=30><b>Transitions</b></size>", false);

			State state;
			if (!myCurrentLayer.myStates.TryGetValue(myMainState.val, out state))
			{
				CreateMenuInfo("You need to add some states before you can add transitions.", 100, false);
				return;
			}
			
			// collect transitions
			List<UITransition> transitions = new List<UITransition>(state.myTransitions.Count);
			for (int i=0; i<state.myTransitions.Count; ++i)
				transitions.Add(new UITransition(state.myTransitions[i], false, true));
			
			foreach (var s in myCurrentLayer.myStates)
			{
				State target = s.Value;
				if (state == target || !target.myTransitions.Contains(state))
					continue;
				
				int idx = transitions.FindIndex(t => t.state == target);
				if (idx >= 0)
					transitions[idx] = new UITransition(target, true, true);
				else
					transitions.Add(new UITransition(target, true, false));
			}
			transitions.Sort((UITransition a, UITransition b) => a.state.myName.CompareTo(b.state.myName));
			
			// collect available targets

			List<string> availableTargets = new List<string>();
			foreach (var t in myCurrentLayer.myStates)
			{
				State target = t.Value;
				if (state == target)
					continue;
				if (transitions.FindIndex(tr => tr.state == target) >= 0)
					continue;
				availableTargets.Add(target.myName);
			}
			availableTargets.Sort();
			
			
			List<string> availableTargetDisplays = new List<string>(availableTargets.Count);
			for (int i=0; i<availableTargets.Count; ++i)
			{
				State target = myCurrentLayer.myStates[availableTargets[i]];
				if (target.IsIntermediate)
					availableTargetDisplays.Add(COLORTAG_INTERMEDIATE+target.myName+"</color>");
				else if (target.IsControlPoint)
					availableTargetDisplays.Add(COLORTAG_CONTROLPOINT+target.myName+"</color>");
				else
					availableTargetDisplays.Add(target.myName);
			}
			myTransitionList.choices = availableTargets;
			myTransitionList.displayChoices = availableTargetDisplays;
			if (availableTargets.Count == 0)
				myTransitionList.val = "";
			else if (!availableTargets.Contains(myTransitionList.val))
				myTransitionList.val = availableTargets[0];

			if (availableTargets.Count > 0)
			{
				CreateMenuPopup(myTransitionList, false);
				CreateMenuButton("Add Transition", UIAddTransition, false);

				if (transitions.Count > 0)
					CreateMenuSpacer(15, false);
			}
			else if (transitions.Count == 0)
			{
				CreateMenuInfo("You need to add a second state before you can add transitions.", 100, false);
			}
			
			for (int i=0; i<transitions.Count; ++i)
			{
				string label;
				State target = transitions[i].state;
				if (target.IsIntermediate)
					label = COLORTAG_INTERMEDIATE+target.myName+"</color>";
				else if (target.IsControlPoint)
					label = COLORTAG_CONTROLPOINT+target.myName+"</color>";
				else
					label = target.myName;

				CreateMenuLabel2BXButton(
					label, "IN", "OUT", transitions[i].incoming, transitions[i].outgoing,
					(bool v) => UIUpdateTransition(target, state, v),
					(bool v) => UIUpdateTransition(state, target, v),
					() => UIRemoveTransition(state, target), false
				);
			}
			
			
			CreateMenuInfoOneLine("<size=30><b>Transition Settings</b></size>", true);
			
			JSONStorableFloat transitionDuration = new JSONStorableFloat("Transition Duration", DEFAULT_TRANSITION_DURATION, 0.01f, 5.0f, true, true);
			transitionDuration.valNoCallback = state.myTransitionDuration;
			transitionDuration.setCallbackFunction = (float v) => {
				State s = UIGetState();
				if (s != null)
					s.myTransitionDuration = v;
			};
			CreateMenuSlider(transitionDuration, true);
			
			if (state.IsRegularState || state.IsIntermediate)
			{
				JSONStorableFloat easeInDuration = new JSONStorableFloat("EaseIn Duration", DEFAULT_EASEIN_DURATION, 0.0f, 3.0f, true, true);
				easeInDuration.valNoCallback = state.myEaseInDuration;
				easeInDuration.setCallbackFunction = (float v) => {
					State s = UIGetState();
					if (s != null)
						s.myEaseInDuration = v;
				};
				CreateMenuSlider(easeInDuration, true);
				
				JSONStorableFloat easeOutDuration = new JSONStorableFloat("EaseOut Duration", DEFAULT_EASEOUT_DURATION, 0.0f, 3.0f, true, true);
				easeOutDuration.valNoCallback = state.myEaseOutDuration;
				easeOutDuration.setCallbackFunction = (float v) => {
					State s = UIGetState();
					if (s != null)
						s.myEaseOutDuration = v;
				};
				CreateMenuSlider(easeOutDuration, true);
			}
		}

		private void CreateTriggers()
		{
			CreateMenuInfoOneLine("<size=30><b>Internal Triggers</b></size>", false);
			State state;
			if (!myCurrentLayer.myStates.TryGetValue(myMainState.val, out state))
			{
				CreateMenuInfo("You need to add some states before you can add triggers.", 100, false);
			}
			else
			{
				CreateMenuButton("Actions " + state.EnterBeginTrigger.Name, state.EnterBeginTrigger.OpenPanel, false);
				CreateMenuButton("Actions " + state.EnterEndTrigger.Name,   state.EnterEndTrigger.OpenPanel,   false);
				CreateMenuButton("Actions " + state.ExitBeginTrigger.Name,  state.ExitBeginTrigger.OpenPanel,  false);
				CreateMenuButton("Actions " + state.ExitEndTrigger.Name,    state.ExitEndTrigger.OpenPanel,    false);
			}

			CreateMenuInfoOneLine("<size=30><b>External Triggers</b></size>", true);
			CreateMenuInfoScrolling(
				"<i>IdlePoser</i> exposes some external trigger connections, allowing you to control transitions with external logic or syncing multiple <i>IdlePosers</i>:\n\n" +
				"<b>SwitchState</b>: Set to a state name to transition directly to that <i>State</i>.\n\n" +
				"<b>SetStateMask</b>: Exclude a number of <i>StateGroups</i> from possible transitions. For example set to <i>ABC</i> to allow only states of groups A,B and C. Set to <i>!ABC</i> to " +
				"allow only states NOT in groups A, B and C, which is equivalent to setting it to <i>DEFGHN</i>. Letter <i>N</i> stands for the 'None' group. Setting to <i>Clear</i> disables the state mask. Lowercase letters are allowed, too. If <i>IdlePoser</i> happens to be in an ongoing transition at the moment of calling <i>SetStateMask</i>, it will wait for reaching a <i>Regular State</i> before triggering a transition to a valid state that matches the new mask.\n\n" +
				"<b>PartialStateMask</b>: Works the same way as <i>SetStateMask</i>, except only those groups explicitly mentioned are changed. Also you can chain multiple statements by separating them with space, semicolon or pipe characters. For example: <i>AB|!CD</i> to enable A and B, while disabling C and D.\n\n" +
				"<b>TriggerSync</b>: If <i>Wait for TriggerSync</i> is enabled for a <i>State</i>, it will, after its duration finished, wait for this to be called before transitioning to the next <i>State</i>.",
				800, true
			);
		}

		private void CreateOptionsMenu()
		{
			CreateMenuInfoOneLine("<size=30><b>General Options</b></size>", false);
			CreateMenuToggle(myOptionsDefaultToWorldAnchor, false);
			
			CreateMenuInfoOneLine("<size=30><b>Debug Options</b></size>", false);
			CreateMenuInfoOneLine("<color=#ff0000><b>Can cause performance issues!</b></color>", false);

			CreateMenuToggle(myDebugShowInfo, false);
			if (myDebugShowInfo.val)
				CreateMenuInfo(myPlayInfo, 300, false);

			CreateMenuToggle(myDebugShowPaths, false);			
			CreateMenuToggle(myDebugShowTransitions, false);
			CreateMenuToggle(myDebugShowSelectedOnly, false);


			CreateMenuInfoOneLine("<size=30><b>Debug Color Legend</b></size>", true);
			CreateMenuInfoWhiteBackground(
				"<b>Regular States + Paths:\n" +
				"<color=#4363d8>    Group None\n</color>" +
				"<color=#e6194B>    Group A\n</color>" +
				"<color=#3cb44b>    Group B\n</color>" +
				"<color=#ffe119>    Group C\n</color>" +
				"<color=#f032e6>    Group D\n</color>" +
				"<color=#42d4f4>    Group E\n</color>" +
				"<color=#f58231>    Group F\n</color>" +
				"<color=#469990>    Group G\n</color>" +
				"<color=#9A6324>    Group H\n</color>" +				
				"<color=#bfef45>    Group I\n</color>" +
				"<color=#911eb4>    Group J\n</color>" +
				"<color=#000075>    Group K\n</color>" +
				"<color=#808000>    Group L\n</color>" +
				"Control Points:\n<color=#a9a9a9>    grey + smaller size\n</color>" +
				"Intermediate Points:\n<color=#a9a9a9>    grey + normal size\n</color>" +
				"Transitions:\n<color=#a9a9a9>    grey lines\n</color></b>",
				650, true
			);
		}

		private void CreateAnimationsMenu()
		{
			CreateMenuInfoOneLine("<size=30><b>Manage Animations</b></size>", false);

			CreateMenuSpacer(132, true);
			String animationName = "";
			if(myCurrentAnimation != null)
				animationName = myCurrentAnimation.myName;
			JSONStorableString name = new JSONStorableString("Animation Name",
				animationName, UIRenameAnimation);
			CreateMenuTextInput("Animation Name", name, false);

			CreateMenuButton("Add Animation", UIAddAnimation, false);
			// State state;
			// if (!myStates.TryGetValue(myMainState.val, out state))
			// 	return;

			CreateMenuButton("Remove Animation", UIRemoveAnimation, false);
			// CreateMenuToggle(myStateAutoTransition, false);

			// CreateMenuButton("Capture State", UICaptureState, true);
			// CreateMenuButton("Apply Anchors", UIApplyAnchors, true);

			// CreateMenuSpacer(15, false);

			// CreateMenuInfoOneLine("<size=30><b>State Settings</b></size>", false);

			// CreateMenuSyncSpacer(0);

			// JSONStorableString name = new JSONStorableString("State Name", state.myName, UIRenameState);
			// CreateMenuTextInput("Name", name, false);



			// JSONStorableStringChooser typeChooser = new JSONStorableStringChooser("StateType", myStateTypes, myStateTypes[state.myStateType], "State\nType");
			// typeChooser.setCallbackFunction += (string v) => {
			// 	State s = UIGetState();
			// 	if (s != null)
			// 		s.myStateType = myStateTypes.IndexOf(v);
			// 	UIRefreshMenu();
			// };
			// CreateMenuPopup(typeChooser, false);

			// if (state.IsRegularState)
			// {
			// 	JSONStorableStringChooser groupChooser = new JSONStorableStringChooser("StateGroup", myStateGroups, myStateGroups[state.myStateGroup], "State\nGroup");
			// 	groupChooser.setCallbackFunction += (string v) => {
			// 		State s = UIGetState();
			// 		if (s != null)
			// 			s.myStateGroup = myStateGroups.IndexOf(v);
			// 	};
			// 	CreateMenuPopup(groupChooser, false);

			// 	JSONStorableFloat probability = new JSONStorableFloat("Relative Probability", state.myProbability, 0.0f, 1.0f, true, true);
			// 	probability.setCallbackFunction = (float v) => {
			// 		State s = UIGetState();
			// 		if (s != null)
			// 			s.myProbability = v;
			// 	};
			// 	CreateMenuSlider(probability, false);
			// }

			// JSONStorableFloat transitionDuration = new JSONStorableFloat("Transition Duration", state.myTransitionDuration, 0.00f, 5.0f, true, true);
			// transitionDuration.setCallbackFunction = (float v) => {
			// 	State s = UIGetState();
			// 	if (s != null)
			// 		s.myTransitionDuration = v;
			// };
			// CreateMenuSlider(transitionDuration, true);

			// if (state.IsRegularState || state.IsIntermediate)
			// {
			// 	JSONStorableBool waitInfiniteDuration = new JSONStorableBool("Wait Infinite Duration", state.myWaitInfiniteDuration);
			// 	waitInfiniteDuration.setCallbackFunction = (bool v) => {
			// 		State s = UIGetState();
			// 		if (s != null)
			// 		{
			// 			s.myWaitInfiniteDuration = v;
			// 			myDuration = v ? float.MaxValue : UnityEngine.Random.Range(s.myWaitDurationMin, s.myWaitDurationMax);
			// 		}
			// 		UIRefreshMenu();
			// 	};
			// 	CreateMenuToggle(waitInfiniteDuration, true);

			// 	if (!state.myWaitInfiniteDuration)
			// 	{
			// 		JSONStorableBool waitForSync = new JSONStorableBool("Wait for TriggerSync", state.myWaitForSync);
			// 		waitForSync.setCallbackFunction = (bool v) => {
			// 			State s = UIGetState();
			// 			if (s != null)
			// 				s.myWaitForSync = v;
			// 		};
			// 		CreateMenuToggle(waitForSync, true);

			// 		JSONStorableFloat waitDurationMin = new JSONStorableFloat("Wait Duration Min", state.myWaitDurationMin, 0.0f, 300.0f, true, true);
			// 		waitDurationMin.setCallbackFunction = (float v) => {
			// 			State s = UIGetState();
			// 			if (s != null)
			// 				s.myWaitDurationMin = v;
			// 		};
			// 		CreateMenuSlider(waitDurationMin, true);

			// 		JSONStorableFloat waitDurationMax = new JSONStorableFloat("Wait Duration Max", state.myWaitDurationMax, 0.0f, 300.0f, true, true);
			// 		waitDurationMax.setCallbackFunction = (float v) => {
			// 			State s = UIGetState();
			// 			if (s != null)
			// 				s.myWaitDurationMax = v;
			// 		};
			// 		CreateMenuSlider(waitDurationMax, true);
			// 	}
			// }
		}
		private void CreateLayersMenu()
		{
			CreateMenuInfoOneLine("<size=30><b>Manage Layers</b></size>", false);

			String layerName = "";
			if(myCurrentLayer != null)
				layerName = myCurrentLayer.myName;
			JSONStorableString name = new JSONStorableString("Layer Name",
				layerName, UIRenameLayer);

			CreateMenuTextInput("Layer Name", name, false);


			CreateMenuButton("Add Layer", UIAddLayer, false);
			CreateMenuButton("Remove Layer", UIRemoveLayer, false);
		}


		// =======================================================================================

		private List<string> GetAllAtomUIDs()
		{
			// working around VaM weirdness not returning hidden atoms
			SuperController sc = SuperController.singleton;
			bool wasSorted = sc.sortAtomUIDs;
			sc.sortAtomUIDs = false;
			List<string> atomUIDs = sc.GetAtomUIDs();
			sc.sortAtomUIDs = wasSorted;
			return atomUIDs;
		}
		private void UILoadAnimationsJSON(string url)
		{
			JSONClass jc = LoadJSON(url).AsObject;
			if (jc != null)
				UIRefreshMenu();
				LoadAnimations(jc);

			if (myCurrentState != null)
			{
				myMainState.valNoCallback = myCurrentState.myName;
				myMainState.setCallbackFunction(myCurrentState.myName);
			}
			else
			{
				UIRefreshMenu();
			}
		}

		private void UILoadJSON(string url)
		{
			JSONClass jc = LoadJSON(url).AsObject;
			if (jc != null)
				UIRefreshMenu();
				LoadLayer(jc, true);

			if (myCurrentState != null)
			{
				myMainState.valNoCallback = myCurrentState.myName;
				myMainState.setCallbackFunction(myCurrentState.myName);
			}
			else
			{
				UIRefreshMenu();
			}
		}
		private void UISaveAnimationsJSONDialog()
		{
			SuperController sc = SuperController.singleton;
			sc.GetMediaPathDialog(UISaveAnimationsJSON, FILE_EXTENSION, BASE_DIRECTORY, false, true, false, null, false, null, false, false);
			sc.mediaFileBrowserUI.SetTextEntry(true);
			if (sc.mediaFileBrowserUI.fileEntryField != null)
			{
				string filename = ((int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds).ToString();
				sc.mediaFileBrowserUI.fileEntryField.text = filename + "." + FILE_EXTENSION;
				sc.mediaFileBrowserUI.ActivateFileNameField();
			}
		}
		private void UISaveAnimationsJSON(string path)
		{
			if (string.IsNullOrEmpty(path))
				return;
			path = path.Replace('\\', '/');
			SuperController.LogMessage("IdlePoser: Saving as '"+path+"'.");
			JSONClass jc = SaveAnimations();
			SaveJSON(jc, path);
		}

		private void UISaveJSONDialog()
		{
			SuperController sc = SuperController.singleton;
			sc.GetMediaPathDialog(UISaveJSON, FILE_EXTENSION, BASE_DIRECTORY, false, true, false, null, false, null, false, false);
			sc.mediaFileBrowserUI.SetTextEntry(true);
			if (sc.mediaFileBrowserUI.fileEntryField != null)
			{
				string filename = ((int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds).ToString();
				sc.mediaFileBrowserUI.fileEntryField.text = filename + "." + FILE_EXTENSION;
				sc.mediaFileBrowserUI.ActivateFileNameField();
			}
		}

		private void UISaveJSON(string path)
		{
			if (string.IsNullOrEmpty(path))
				return;
			path = path.Replace('\\', '/');
			SuperController.LogMessage("IdlePoser: Saving as '"+path+"'.");
			JSONClass jc = SaveLayer(myCurrentLayer);
			SaveJSON(jc, path);
		}

		private void UIAddControlCapture()
		{
			ControlCapture cc = new ControlCapture(this, myCapturesControlList.val);
			myCurrentLayer.myControlCaptures.Add(cc);
			foreach (var state in myCurrentLayer.myStates)
				cc.CaptureEntry(state.Value);
			UIRefreshMenu();
		}

		private void UIRemoveControlCapture(ControlCapture cc)
		{
			myCurrentLayer.myControlCaptures.Remove(cc);
			foreach (var state in myCurrentLayer.myStates)
				state.Value.myControlEntries.Remove(cc);
			UIRefreshMenu();
		}

		private void UIToggleControlCapturePosition()
		{
			bool apply = false;
			for (int i=0; i<myCurrentLayer.myControlCaptures.Count; ++i)
				apply |= !myCurrentLayer.myControlCaptures[i].myApplyPosition;
			for (int i=0; i<myCurrentLayer.myControlCaptures.Count; ++i)
				myCurrentLayer.myControlCaptures[i].myApplyPosition = apply;
			UIRefreshMenu();
		}

		private void UIToggleControlCaptureRotation()
		{
			bool apply = false;
			for (int i=0; i<myCurrentLayer.myControlCaptures.Count; ++i)
				apply |= !myCurrentLayer.myControlCaptures[i].myApplyRotation;
			for (int i=0; i<myCurrentLayer.myControlCaptures.Count; ++i)
				myCurrentLayer.myControlCaptures[i].myApplyRotation = apply;
			UIRefreshMenu();
		}

		private void UIAddMorphCapture()
		{
			DAZCharacterSelector geometry = containingAtom.GetStorableByID("geometry") as DAZCharacterSelector;
			if (geometry == null)
				return;

			string qualifiedName = myCapturesMorphList.val;
			bool morphIsMale = qualifiedName.EndsWith("#Male");
			if (!morphIsMale && !qualifiedName.EndsWith("#Female"))
				return;

			string uid = qualifiedName.Substring(0, qualifiedName.Length - (morphIsMale ? 5 : 7));
			GenerateDAZMorphsControlUI morphControl = morphIsMale ? geometry.morphsControlMaleUI : geometry.morphsControlFemaleUI;
			DAZMorph morph =  morphControl.GetMorphByUid(uid);
			if (morph == null)
				return;

			DAZCharacterSelector.Gender gender = morphIsMale ? DAZCharacterSelector.Gender.Male : DAZCharacterSelector.Gender.Female;
			if (myCurrentLayer.myMorphCaptures.FindIndex(x => x.myMorph == morph && x.myGender == gender) >= 0)
				return;

			MorphCapture mc = new MorphCapture(gender, morph);
			myCurrentLayer.myMorphCaptures.Add(mc);
			foreach (var state in myCurrentLayer.myStates)
				mc.CaptureEntry(state.Value);

			UIRefreshMenu();
		}

		private void UIUpdateMorphFullename(string qualifiedName)
		{
			myCapturesMorphFullname.val = "";

			DAZCharacterSelector geometry = containingAtom.GetStorableByID("geometry") as DAZCharacterSelector;
			if (geometry == null)
				return;

			bool personIsMale = geometry.gender == DAZCharacterSelector.Gender.Male;
			bool morphIsMale = qualifiedName.EndsWith("#Male");
			if (!morphIsMale && !qualifiedName.EndsWith("#Female"))
				return;

			string uid = qualifiedName.Substring(0, qualifiedName.Length - (morphIsMale ? 5 : 7));
			GenerateDAZMorphsControlUI morphControl = morphIsMale ? geometry.morphsControlMaleUI : geometry.morphsControlFemaleUI;
			DAZMorph morph =  morphControl.GetMorphByUid(uid);
			if (morph == null)
				return;

			StringBuilder builder = new StringBuilder(256);
			int idx = uid.IndexOf(":/");
			if (idx >= 0)
				builder.Append("<i><color=#FF8080>").Append(uid.Substring(0, idx)).Append("</color> / ");
			else
				builder.Append("<i><color=#808080>Local Morph</color> / ");

			builder.Append(morphIsMale == personIsMale ? "<color=#808080>" :  "<color=#FF8080>");
			builder.Append(morphIsMale ? "Male</color>\n" : "Female</color>\n");
			builder.Append("<color=#808080>").Append(morph.resolvedRegionName).Append("</color></i>\n");
			builder.Append(morph.resolvedDisplayName);

			myCapturesMorphFullname.val = builder.ToString();
		}

		private void UICollectMorphsUIDs(DAZCharacterSelector.Gender gender, DAZCharacterSelector geometry, List<string> collectedMorphUIDs)
		{
			bool isMale = gender == DAZCharacterSelector.Gender.Male;
			GenerateDAZMorphsControlUI morphsControl = isMale ? geometry.morphsControlMaleUI : geometry.morphsControlFemaleUI;
			List<DAZMorph> morphs = morphsControl.GetMorphs();
			for (int i=0; i<morphs.Count; ++i)
			{
				if (!morphs[i].visible)
					continue;
				string uid = morphs[i].uid.Replace(isMale ? "Custom/Atom/Person/Morphs/male/" : "Custom/Atom/Person/Morphs/female/", "&&");
				collectedMorphUIDs.Add(uid + (isMale ? "#Male" : "#Female"));
			}
		}

		private void UICollectMorphsNames(DAZCharacterSelector geometry, List<string> collectedMorphUIDs, List<string> collectedMorphsNames)
		{
			int maxPackageLen = 23;

			bool personIsMale = geometry.gender == DAZCharacterSelector.Gender.Male;
			StringBuilder builder = new StringBuilder(256);
			for (int i=0; i<collectedMorphUIDs.Count; ++i)
			{
				string qualifiedName = collectedMorphUIDs[i];
				bool morphIsMale = qualifiedName.EndsWith("#Male");
				qualifiedName = qualifiedName.Replace("&&", morphIsMale ? "Custom/Atom/Person/Morphs/male/" : "Custom/Atom/Person/Morphs/female/");
				collectedMorphUIDs[i] = qualifiedName;
				string uid = qualifiedName.Substring(0, qualifiedName.Length - (morphIsMale ? 5 : 7));
				int idx = uid.IndexOf(":/");
				if (idx > maxPackageLen)
				{
					builder.Append("<i><color=#FF8080>");
					builder.Append(uid.Substring(0, 3));
					builder.Append("...");
					builder.Append(uid.Substring(idx-maxPackageLen+6, maxPackageLen-6));
					builder.Append("</color> / ");
				}
				else if (idx >= 0)
				{
					builder.Append("<i><color=#FF8080>");
					builder.Append(uid.Substring(0, idx));
					builder.Append("</color> / ");
				}
				else
				{
					builder.Append("<i><color=#808080>Local Morph</color> / ");
				}

				builder.Append(personIsMale == morphIsMale ? "<color=#808080>" : "<color=#FF8080>");
				builder.Append(morphIsMale ? "M</color></i>\n" : "F</color></i>\n");

				GenerateDAZMorphsControlUI morphControl = morphIsMale ? geometry.morphsControlMaleUI : geometry.morphsControlFemaleUI;
				DAZMorph morph =  morphControl.GetMorphByUid(uid);
				if (morph != null)
					builder.Append(morph.resolvedDisplayName);

				collectedMorphsNames.Add(builder.ToString());
				builder.Length = 0;
			}
		}

		private void UIRemoveMorphCapture(MorphCapture mc)
		{
			myCurrentLayer.myMorphCaptures.Remove(mc);
			foreach (var state in myCurrentLayer.myStates)
				state.Value.myMorphEntries.Remove(mc);
			UIRefreshMenu();
		}

		private void UIToggleMorphCapture()
		{
			bool apply = false;
			for (int i=0; i<myCurrentLayer.myMorphCaptures.Count; ++i)
				apply |= !myCurrentLayer.myMorphCaptures[i].myApply;
			for (int i=0; i<myCurrentLayer.myMorphCaptures.Count; ++i)
				myCurrentLayer.myMorphCaptures[i].myApply = apply;
			UIRefreshMenu();
		}

		private void UIAddAnimation()
		{
			for (int i=1; i<1000; ++i)
			{
				string name = "Animation#" + i;
				if (!myAnimations.ContainsKey(name))
				{
					CreateAnimation(name);
					myMainAnimation.val = name;
					UIRefreshMenu();
					return;
				}
			}
			SuperController.LogError("IdlePoser: Too many animations!");
		}

		private void UIAddLayer()
		{
			for (int i=1; i<1000; ++i)
			{
				string name = "Layer#" + i;
				if (!myCurrentAnimation.myLayers.ContainsKey(name))
				{
					CreateLayer(name);
					myMainLayer.val = name;
					UIRefreshMenu();
					return;
				}
			}
			SuperController.LogError("IdlePoser: Too many layers!");
		}

		private void UIAddState()
		{
			for (int i=1; i<1000; ++i)
			{
				string name = "State#" + i;
				if (!myCurrentLayer.myStates.ContainsKey(name))
				{
					CreateState(name);
					myMainState.val = name;
					UIRefreshMenu();
					return;
				}
			}
			SuperController.LogError("IdlePoser: Too many states!");
		}

		private void UICaptureState()
		{
			State state = UIGetState();
			if (state == null)
				return;

			CaptureState(state);
			UIRefreshMenu();
		}

		private void UIApplyAnchors()
		{
			foreach (var s in myCurrentLayer.myStates)
			{
				State state = s.Value;
				foreach (var ce in state.myControlEntries)
				{
					ControlEntryAnchored entry = ce.Value;
					entry.UpdateInstant();
				}
			}

			for (int i=0; i<myCurrentLayer.myControlCaptures.Count; ++i)
				myCurrentLayer.myControlCaptures[i].UpdateState(myCurrentState);
		}

		private void UIRemoveAnimation()
		{
			Animation animation = UIGetAnimation();
			if (animation == null)
				return;

			myAnimations.Remove(animation.myName);
			// foreach (var s in myStates)
			// 	s.Value.myTransitions.RemoveAll(x => x == state);

			// state.EnterBeginTrigger.Remove();
			// state.EnterEndTrigger.Remove();
			// state.ExitBeginTrigger.Remove();
			// state.ExitEndTrigger.Remove();

			UIRefreshMenu();
		}
		private void UIRemoveLayer()
		{
			Layer layer = UIGetLayer();
			if (layer == null)
				return;

			myCurrentAnimation.myLayers.Remove(layer.myName);
			// foreach (var s in myStates)
			// 	s.Value.myTransitions.RemoveAll(x => x == state);

			// state.EnterBeginTrigger.Remove();
			// state.EnterEndTrigger.Remove();
			// state.ExitBeginTrigger.Remove();
			// state.ExitEndTrigger.Remove();

			UIRefreshMenu();
		}

		private void UIRemoveState()
		{
			State state = UIGetState();
			if (state == null)
				return;

			myCurrentLayer.myStates.Remove(state.myName);
			foreach (var s in myCurrentLayer.myStates)
				s.Value.myTransitions.RemoveAll(x => x == state);

			state.EnterBeginTrigger.Remove();
			state.EnterEndTrigger.Remove();
			state.ExitBeginTrigger.Remove();
			state.ExitEndTrigger.Remove();

			UIRefreshMenu();
		}

		private void UIRenameAnimation(string name)
		{
			if (myCurrentAnimation == null)
				return;
			if (myCurrentAnimation.myName == name)
				return;

			myAnimations.Remove(myCurrentAnimation.myName);

			// int altIndex = 2;
			// string baseName = name;
			// while (myCurrentLayer.myStates.ContainsKey(name))
			// {
			// 	name = baseName + "#" + altIndex;
			// 	++altIndex;
			// }

			myAnimations.Add(name, myCurrentAnimation);
			myCurrentAnimation.myName = name;
			UIRefreshMenu();
		}

		private void UIRenameLayer(string name)
		{
			if (myCurrentLayer == null)
				return;
			if (myCurrentLayer.myName == name)
				return;

			myCurrentAnimation.myLayers.Remove(myCurrentLayer.myName);

			myCurrentAnimation.myLayers.Add(name, myCurrentLayer);
			myCurrentLayer.myName = name;
			UIRefreshMenu();
		}

		private void UIRenameState(string name)
		{
			State state = UIGetState();
			if (state == null)
				return;
			if (state.myName == name)
				return;

			myCurrentLayer.myStates.Remove(state.myName);

			int altIndex = 2;
			string baseName = name;
			while (myCurrentLayer.myStates.ContainsKey(name))
			{
				name = baseName + "#" + altIndex;
				++altIndex;
			}

			myCurrentLayer.myStates.Add(name, state);
			state.myName = name;
			state.EnterBeginTrigger.SecondaryName = name;
			state.EnterEndTrigger.SecondaryName = name;
			state.ExitBeginTrigger.SecondaryName = name;
			state.ExitEndTrigger.SecondaryName = name;
			myMainState.valNoCallback = name;
			UIRefreshMenu();
		}

		private void UISetAnchors()
		{
			State state = UIGetState();
			if (state == null)
				return;

			ControlCapture controlCapture = myCurrentLayer.myControlCaptures.Find(cc => cc.myName == myAnchorCaptureList.val);
			if (controlCapture == null)
				return;

			ControlEntryAnchored controlEntry;
			if (!state.myControlEntries.TryGetValue(controlCapture, out controlEntry))
				return;
				
			controlEntry.UpdateInstant();

			int anchorMode = myAnchorModes.FindIndex(m => m == myAnchorModeList.val);
			if (anchorMode >= 0)
				controlEntry.myAnchorMode = anchorMode;
			controlEntry.myBlendRatio = myAnchorBlendRatio.val;
			controlEntry.myDampingTime = myAnchorDampingTime.val;

			if (anchorMode >= ControlEntryAnchored.ANCHORMODE_SINGLE)
			{
				controlEntry.myAnchorAAtom = myAnchorAtomListA.val;
				controlEntry.myAnchorAControl = myAnchorControlListA.val;
			}
			if (anchorMode == ControlEntryAnchored.ANCHORMODE_BLEND)
			{
				controlEntry.myAnchorBAtom = myAnchorAtomListB.val;
				controlEntry.myAnchorBControl = myAnchorControlListB.val;
			}
			
			controlEntry.AdjustAnchor();

			UIRefreshMenu();
		}

		private void UISetAnchorsBlendDamp()
		{
			State state = UIGetState();
			if (state == null)
				return;

			ControlCapture controlCapture = myCurrentLayer.myControlCaptures.Find(cc => cc.myName == myAnchorCaptureList.val);
			if (controlCapture == null)
				return;

			ControlEntryAnchored controlEntry;
			if (!state.myControlEntries.TryGetValue(controlCapture, out controlEntry))
				return;

			controlEntry.myBlendRatio = myAnchorBlendRatio.val;
			controlEntry.myDampingTime = myAnchorDampingTime.val;
			controlEntry.AdjustAnchor();
		}

		private void UIAddTransition()
		{
			State source = UIGetState();
			if (source == null)
				return;
			State target;
			if (!myCurrentLayer.myStates.TryGetValue(myTransitionList.val, out target))
				return;

			if (!source.myTransitions.Contains(target))
				source.myTransitions.Add(target);
			if (!target.myTransitions.Contains(source))
				target.myTransitions.Add(source);

			UIRefreshMenu();
		}
		
		private void UIUpdateTransition(State source, State target, bool transitionEnabled)
		{
			if (transitionEnabled && !source.myTransitions.Contains(target))
				source.myTransitions.Add(target);
			else if (!transitionEnabled)
				source.myTransitions.Remove(target);
		}

		private void UIRemoveTransition(State source, State target)
		{
			source.myTransitions.Remove(target);
			target.myTransitions.Remove(source);
			UIRefreshMenu();
		}

		private Animation UIGetAnimation()
		{
			Animation animation;
			if (!myAnimations.TryGetValue(myMainAnimation.val, out animation))
			{
				SuperController.LogError("IdlePoser: Invalid animation selected!");
				return null;
			}
			else
			{
				return animation;
			}
		}

		private Layer UIGetLayer()
		{
			Layer layer;
			if (!myCurrentAnimation.myLayers.TryGetValue(myMainLayer.val, out layer))
			{
				SuperController.LogError("IdlePoser: Invalid layer selected!");
				return null;
			}
			else
			{
				return layer;
			}
		}

		private State UIGetState()
		{
			State state;
			if (!myCurrentLayer.myStates.TryGetValue(myMainState.val, out state))
			{
				SuperController.LogError("IdlePoser: Invalid state selected!");
				return null;
			}
			else
			{
				return state;
			}
		}

		// =======================================================================================


		private void CreateMenuButton(string label, UnityAction callback, bool rightSide)
		{
			UIDynamicButton uid = Utils.SetupButton(this, label, callback, rightSide);
			myMenuElements.Add(uid);
		}

		private void CreateMenuLabelXButton(string label, UnityAction callback, bool rightSide)
		{
			UIDynamicLabelXButton uid = Utils.SetupLabelXButton(this, label, callback, rightSide);
			myMenuElements.Add(uid);
		}

		private void CreateMenuLabel2BXButton(string label, string text1, string text2, bool value1, bool value2,
		                                      UnityAction<bool> callback1, UnityAction<bool> callback2, UnityAction callbackX, bool rightSide)
		{
			if (myLabelWith2BXButtonPrefab == null)
				InitPrefabs();

			Transform t = CreateUIElement(myLabelWith2BXButtonPrefab.transform, rightSide);
			UIDynamicLabel2BXButton uid = t.GetComponent<UIDynamicLabel2BXButton>();
			uid.label.text = label;
			uid.text1.text = text1;
			uid.text2.text = text2;

			Color color1 = uid.image1.color;
			color1.a = value1 ? 1.0f : 0.0f;
			uid.image1.color = color1;
			uid.toggle1.isOn = value1;
			uid.toggle1.onValueChanged.AddListener((bool v) => {
				Color color = uid.image1.color;
				color.a = uid.toggle1.isOn ? 1.0f : 0.0f;
				uid.image1.color = color;
				callback1(v);
			});

			Color color2 = uid.image2.color;
			color2.a = value2 ? 1.0f : 0.0f;
			uid.image2.color = color2;
			uid.toggle2.isOn = value2;
			uid.toggle2.onValueChanged.AddListener((bool v) => {
				Color color = uid.image2.color;
				color.a = uid.toggle2.isOn ? 1.0f : 0.0f;
				uid.image2.color = color;
				callback2(v);
			});
			uid.buttonX.onClick.AddListener(callbackX);

			myMenuElements.Add(uid);
			t.gameObject.SetActive(true);
		}

		private void CreateMenuLabelMXButton(string label, bool valueM, UnityAction<bool> mCallback, UnityAction callbackX, bool rightSide)
		{
			if (myLabelWithMXButtonPrefab == null)
				InitPrefabs();

			Transform t = CreateUIElement(myLabelWithMXButtonPrefab.transform, rightSide);
			UIDynamicLabelMXButton uid = t.GetComponent<UIDynamicLabelMXButton>();
			uid.label.text = label;

			Color colorM = uid.imageM.color;
			colorM.a = valueM ? 1.0f : 0.0f;
			uid.imageM.color = colorM;
			uid.toggleM.isOn = valueM;
			uid.toggleM.onValueChanged.AddListener((bool v) => {
				Color color = uid.imageM.color;
				color.a = uid.toggleM.isOn ? 1.0f : 0.0f;
				uid.imageM.color = color;
				mCallback(v);
			});

			uid.buttonX.onClick.AddListener(callbackX);

			myMenuElements.Add(uid);
			t.gameObject.SetActive(true);
		}

		private void CreateMenuTwinButton(string leftLabel, UnityAction leftCallback, string rightLabel, UnityAction rightCallback, bool rightSide)
		{
			UIDynamicTwinButton uid = Utils.SetupTwinButton(this, leftLabel, leftCallback, rightLabel, rightCallback, rightSide);
			myMenuElements.Add(uid);
		}

		private void CreateMenuToggle(JSONStorableBool storable, bool rightSide)
		{
			CreateToggle(storable, rightSide);
			myMenuElements.Add(storable);
		}

		private void CreateMenuSlider(JSONStorableFloat storable, bool rightSide)
		{
			CreateSlider(storable, rightSide);
			myMenuElements.Add(storable);
		}

		private void CreateMenuPopup(JSONStorableStringChooser storable, bool rightSide)
		{
			CreateScrollablePopup(storable, rightSide);
			myMenuElements.Add(storable);
		}
		
		private void CreateMenuFilterPopup(JSONStorableStringChooser storable, bool rightSide)
		{
			CreateFilterablePopup(storable, rightSide);
			myMenuElements.Add(storable);
		}

		private void CreateMenuFilterPopupCached(JSONStorableStringChooser storable, bool rightSide, ref UIDynamicPopup cachedPopup)
		{
			if (cachedPopup == null)
			{
				cachedPopup = CreateFilterablePopup(storable, rightSide);
				myMenuCached.Add(cachedPopup);
			}
			else
			{
				cachedPopup.transform.SetAsLastSibling();
				cachedPopup.gameObject.SetActive(true);
			}
		}

		private void CreateMenuTextInput(string label, JSONStorableString storable, bool rightSide)
        {
			UIDynamicLabelInput uid = Utils.SetupTextInput(this, label, storable, rightSide);
			myMenuElements.Add(uid);
        }

		private void CreateMenuInfo(string text, float height, bool rightSide)
		{
			UIDynamicTextInfo uid = Utils.SetupInfoTextNoScroll(this, text, height, rightSide);
			myMenuElements.Add(uid);
		}

		private void CreateMenuInfo(JSONStorableString storable, float height, bool rightSide)
		{
			UIDynamicTextInfo uid = Utils.SetupInfoTextNoScroll(this, storable, height, rightSide);
			myMenuElements.Add(uid);
		}

		private void CreateMenuInfoWhiteBackground(string text, float height, bool rightSide)
		{
			UIDynamicTextInfo uid = Utils.SetupInfoTextNoScroll(this, text, height, rightSide);
			uid.background.GetComponent<Image>().color = Color.white;
			myMenuElements.Add(uid);
		}

		private void CreateMenuInfoScrolling(string text, float height, bool rightSide)
		{
			JSONStorableString storable = Utils.SetupInfoText(this, text, height, rightSide);
			myMenuElements.Add(storable);
		}

		private void CreateMenuInfoOneLine(string text, bool rightSide)
		{
			UIDynamicTextInfo uid = Utils.SetupInfoOneLine(this, text, rightSide);
			myMenuElements.Add(uid);
		}

		private void CreateMenuSpacer(float height, bool rightSide)
		{
			UIDynamic spacer = CreateSpacer(rightSide);
			spacer.height = height;
			myMenuElements.Add(spacer);
		}

		private void CleanupMenu()
		{
			Utils.RemoveUIElements(this, myMenuElements);

			for (int i=0; i<myMenuCached.Count; ++i)
			{
				UIDynamic uid = myMenuCached[i];
				if (uid is UIDynamicPopup)
					((UIDynamicPopup)uid).popup.visible = false;
				uid.gameObject.SetActive(false);
			}
		}


		// =======================================================================================

		private class UIDynamicLabel2BXButton : UIDynamicUtils
		{
			public Text label;
			public Image image1;
			public Image image2;
			public Toggle toggle1;
			public Toggle toggle2;
			public Text text1;
			public Text text2;
			public Button buttonX;
		}

		private class UIDynamicLabelMXButton : UIDynamicUtils
		{
			public Text label;
			public Image imageM;
			public Toggle toggleM;
			public Button buttonX;
		}

		private class UIDynamicTabBar : UIDynamicUtils
		{
			public List<Button> buttons = new List<Button>();
		}
		
		private struct UITransition
		{
			public State state;
			public bool incoming;
			public bool outgoing;
			
			public UITransition(State state, bool incoming, bool outgoing)
			{
				this.state = state;
				this.incoming = incoming;
				this.outgoing = outgoing;
			}
		}		
	}

}