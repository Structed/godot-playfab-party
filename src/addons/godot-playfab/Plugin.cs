#if TOOLS
using Godot;

[Tool]
public partial class Plugin : EditorPlugin
{
	private readonly PackedScene mainPanel;
	private Node mainPanelInstance;
	public Plugin()
	{
		this.mainPanel = GD.Load<PackedScene>("res://addons/godot-playfab/Scenes/Editor/EditorMain.tscn");
	}

	public override void _EnterTree()
	{
		// Initialization of the plugin goes here.
		GD.Print("From Editor");	// TODO remove that line

		this.mainPanelInstance = mainPanel.Instantiate();
		// Add the main panel to the editor's main viewport.

		GetEditorInterface().GetEditorMainScreen().AddChild(mainPanelInstance);
		// Hide the main panel. Very much required.
		_MakeVisible(false);
	}

	public override void _ExitTree()
	{
		mainPanelInstance?.QueueFree();
	}

	public override bool _HasMainScreen()
	{
		return true;
	}

	public override void _MakeVisible(bool visible)
	{
		if (mainPanel != null)
		{
			(mainPanelInstance as Control).Visible = visible;
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
}
#endif
