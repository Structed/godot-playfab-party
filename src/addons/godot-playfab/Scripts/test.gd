extends Object
class_name test

var pf_api = load("res://addons/godot-playfab/Scripts/PlayFabApi.cs")


func Login():
	var pfapi = pf_api.new()
	var client_instance_api = pfapi.ClientInstanceApi()
	pfapi.Instance.LoginWithCustomIDAsync();
	pass
