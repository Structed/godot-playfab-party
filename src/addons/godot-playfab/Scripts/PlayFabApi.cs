
public partial class PlayFabApi : Godot.Object
{
    private static PlayFabApi instance;
    private PlayFabApi()
    {

    }

    public static PlayFabApi Instance => instance ??= new PlayFabApi();
}
