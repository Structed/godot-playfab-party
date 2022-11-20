using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PlayFab;
using PlayFab.ClientModels;
using PlayFab.Party;

public partial class PlayFabManager : Node
{
    List<PlayFabMultiplayerManager> multiplayerManagers = new();

    public string TitleId { get; private set; }

    public PlayFabManager()
    {
        // ProjectSettings.get_setting(PlayFabConstants.SETTING_PLAYFAB_TITLE_ID)
        // GDScript PlayFabConstants = (GDScript) GD.Load("res://addons/godot-playfab/Scripts/PlayFabConstants.gd");
        this.TitleId = ProjectSettings.GetSetting("playfab/title_id").ToString();
    }

    public async Task<PlayFabAuthenticationContext> AddPlayer(string playerName)
    {
        var clientApi = new PlayFabClientInstanceAPI();
        var loginRequest = new LoginWithCustomIDRequest()
        {
            CustomId = playerName,
        };
        var response = await clientApi.LoginWithCustomIDAsync(loginRequest);
        if (response.Error != null)
        {
            throw new Exception(response.Error.GenerateErrorReport());
        }

        var eventsApi = new PlayFabEventsInstanceAPI(response.Result.AuthenticationContext);

        var multiplayerManager = new PlayFabMultiplayerManager(eventsApi, response.Result.AuthenticationContext);
        this.multiplayerManagers.Add(multiplayerManager);

        return response.Result.AuthenticationContext;
    }
}
