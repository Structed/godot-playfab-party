@tool
extends Control


func _ready():
	if ProjectSettings.has_setting(PlayFabConstants.SETTING_PLAYFAB_TITLE_ID):
		var title_id = ProjectSettings.get_setting(PlayFabConstants.SETTING_PLAYFAB_TITLE_ID)
		print(title_id)
		$TitleIdContainer/TitleIdLineEdit.text = title_id


func _on_title_id_line_edit_text_changed(new_text):
	print("signalled")
	update_title_id_setting()


func update_title_id_setting():
	var title_id = $TitleIdContainer/TitleIdLineEdit.text
	if ProjectSettings.has_setting(PlayFabConstants.SETTING_PLAYFAB_TITLE_ID):
		ProjectSettings.set_setting(PlayFabConstants.SETTING_PLAYFAB_TITLE_ID, title_id)
	else:
		add_custom_project_setting(PlayFabConstants.SETTING_PLAYFAB_TITLE_ID, title_id, TYPE_STRING, PROPERTY_HINT_PLACEHOLDER_TEXT, "Retieve from PlayFab Game Manager")

		var error: int = ProjectSettings.save()
		if error: push_error("Encountered error %d when saving project settings." % error)

	print("Saved setting %s" % title_id)


func add_custom_project_setting(name: String, default_value, type: int, hint: int = PROPERTY_HINT_NONE, hint_string: String = "") -> void:
	if ProjectSettings.has_setting(name): return

	var setting_info: Dictionary = {
		"name": name,
		"type": type,
		"hint": hint,
		"hint_string": hint_string
	}

	ProjectSettings.add_property_info(setting_info)
	ProjectSettings.set_initial_value(name, default_value)
	ProjectSettings.set_setting(name, default_value)
