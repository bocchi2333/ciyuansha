extends SceneTree

const TEXTURE_DIR := "res://Assets/FX/Textures/"
const AUDIO_DIR := "res://Assets/FX/Audio/"
const PACK_PATH := "E:/SteamLibrary/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck"
const PACK_SOURCE_DIR := "res://.godot/imported/"

const TEXTURES := {
	"base_glow.png-382d5748a9ad57b75c7b5670d96de850.ctex": "base_glow.png",
	"big_slash_impact_smoke_flipbook.png-bf43229d8653b7d0cca2fdabad9ecf6f.ctex": "slash_smoke_flipbook.png",
	"bloodyimpact_anim_all.png-352801469a8ecc394826592d1efb0ea8.ctex": "bloody_impact_flipbook.png",
	"common_impact_flare_flipbook.png-c9c99bf8237f76b1a2e4824e479a241d.ctex": "impact_flare_flipbook.png",
	"common_round_smoke_flipbook.png-08a2bfa70d47a7172675e30ebbec027b.ctex": "round_smoke_flipbook.png",
	"fire_impact_flipbook.png-59faa29304a0d4611b6a5b8f88ce04d0.ctex": "fire_impact_flipbook.png",
	"ground_spark.png-491a72c9af47967e6ee82964648cbc36.ctex": "ground_spark.png",
	"intent_heal.png-ec05ca102c161b71d91ef8aae48d5647.ctex": "intent_heal.png",
	"lightning_flipbook_1.png-5cea12987abd1ffdb8c1e6421c7fa55e.ctex": "lightning_flipbook.png",
}

const AUDIO := {
	"battle_start_1.mp3-9b591fdbcfa11858ece735905b44d601.mp3str": "battle_start.mp3",
	"card_deal.mp3-c0981c796fa9900913006b74f202f30e.mp3str": "card_deal.mp3",
	"card_select.mp3-1dd5f2cdd037cf3bc133d85c63c302eb.mp3str": "card_select.mp3",
	"dark_orb_evoke.mp3-6dbc753003fb2b1ce7a283758feb9bfb.mp3str": "physical_impact.mp3",
	"gain_potion.mp3-887e2efd2a1d28220c4f5d929337f717.mp3str": "heal.mp3",
	"lightning_orb_evoke.mp3-e6bd5a1f285f34a67d7ed8cf982a73f6.mp3str": "thunder.mp3",
	"STS_DeathStinger_v4_Short_SFX.mp3-a76f07e7a07023da43d677d5c65c496c.mp3str": "defeat.mp3",
	"STS_SFX_BurnCard_v1.mp3-9476eba50d3f88ff7f3e6dde92b55f57.mp3str": "fire.mp3",
}


func _initialize() -> void:
	var pack_loaded := ProjectSettings.load_resource_pack(PACK_PATH, false)
	if not pack_loaded:
		push_error("Unable to mount authorized resource pack: " + PACK_PATH)
		quit(1)
		return

	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(TEXTURE_DIR))
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(AUDIO_DIR))
	var failures := 0

	for source_name: String in TEXTURES:
		var texture := ResourceLoader.load(PACK_SOURCE_DIR + source_name) as Texture2D
		if texture == null:
			push_error("Unable to load texture cache: " + source_name)
			failures += 1
			continue
		var image := texture.get_image()
		var target_path: String = TEXTURE_DIR + TEXTURES[source_name]
		var save_error := image.save_png(ProjectSettings.globalize_path(target_path))
		if save_error != OK:
			push_error("Unable to save texture: " + target_path)
			failures += 1
		else:
			print("Converted texture ", source_name, " -> ", target_path, " (", image.get_width(), "x", image.get_height(), ")")

	for source_name: String in AUDIO:
		var stream := ResourceLoader.load(PACK_SOURCE_DIR + source_name) as AudioStreamMP3
		if stream == null:
			push_error("Unable to load audio cache: " + source_name)
			failures += 1
			continue
		var target_path: String = AUDIO_DIR + AUDIO[source_name]
		var output := FileAccess.open(ProjectSettings.globalize_path(target_path), FileAccess.WRITE)
		if output == null:
			push_error("Unable to open audio target: " + target_path)
			failures += 1
			continue
		output.store_buffer(stream.data)
		print("Converted audio ", source_name, " -> ", target_path, " (", stream.data.size(), " bytes)")

	quit(failures)
