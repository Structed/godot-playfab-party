#if TOOLS
using Godot;
using System;
using Godot.Collections;
using godotplayfabparty.Scripts;

[Tool]
public partial class Plugin : EditorPlugin
{
private readonly PackedScene mainPanel;
	private Node mainPanelInstance;
	public Plugin()
	{
		this.AddCustomProjectSetting(PlayFabConstants.SETTING_PLAYFAB_TITLE_ID, "", Variant.Type.String, PropertyHint.PlaceholderText, "Retrieve from PlayFab Game Manager");
		Error error = ProjectSettings.Save();
		if (error != Error.Ok)
		{
			// TODO decide between exception and push error
			var message = $"Encountered error {Enum.GetName(error)} when saving project settings.";
			GD.PushError(message);
			throw new Exception(message);
		}

		this.mainPanel = GD.Load<PackedScene>("res://Scenes/Editor/EditorMain.tscn");
	}

	public override void _EnterTree()
	{
		// Initialization of the plugin goes here.
		GD.Print("From Editor");	// TODO remove that line

		this.mainPanelInstance = mainPanel.Instantiate();
		// Add the main panel to the editor's main viewport.

		GetEditorInterface().GetViewport().AddChild(mainPanelInstance);
		// Hide the main panel. Very much required.
		_MakeVisible(false);
	}

	public override void _ExitTree()
	{
		// Clean-up of the plugin goes here.
	}

	public override bool _HasMainScreen()
	{
		return true;
	}

	public override void _MakeVisible(bool visible)
	{
		if (mainPanel != null)
		{
			mainPanelInstance.QueueFree();
		}
	}

	public override string _GetPluginName()
	{
		return "PlayFab";
	}

	public override Texture2D _GetPluginIcon()
	{
		return GD.Load<Texture2D>("res://icon_16x16.png");
	}

	private void AddCustomProjectSetting(string name, string defaultValue, Variant.Type type, PropertyHint propertyHint, string hintText = "")
	{
		if (ProjectSettings.HasSetting(name))
		{
			return;
		}

		var propertyInfo = new Dictionary();
		propertyInfo.Add("name", name);
		propertyInfo.Add("type", Enum.GetName(type));
		propertyInfo.Add("hint", Enum.GetName(propertyHint));
		propertyInfo.Add("hint_string", hintText);

		ProjectSettings.SetSetting(name, defaultValue);
		ProjectSettings.AddPropertyInfo(propertyInfo);
		ProjectSettings.SetInitialValue(name, defaultValue);
	}
}
#endif
