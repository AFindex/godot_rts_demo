shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_disabled;

#include "res://war3_rts/terrain/shaders/War3Fog.gdshaderinc"

uniform sampler2D terrain_atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec3 color_tint : source_color = vec3(1.0);
uniform float macro_uv_warp : hint_range(0.0, 0.12) = 0.035;
uniform float macro_tint : hint_range(0.0, 0.12) = 0.025;

float cell_hash(vec2 cell, float salt) {
	return fract(sin(dot(cell + salt, vec2(127.1, 311.7))) * 43758.5453);
}

float value_noise(vec2 position, float salt) {
	vec2 cell = floor(position);
	vec2 local = fract(position);
	local = local * local * (3.0 - 2.0 * local);
	float a = cell_hash(cell, salt);
	float b = cell_hash(cell + vec2(1.0, 0.0), salt);
	float c = cell_hash(cell + vec2(0.0, 1.0), salt);
	float d = cell_hash(cell + vec2(1.0, 1.0), salt);
	return mix(mix(a, b, local.x), mix(c, d, local.x), local.y);
}

float weighted_variation(vec2 cell) {
	float roll = floor(cell_hash(cell, 3.7) * 570.0);
	if (roll < 85.0) return 0.0;
	if (roll < 170.0) return 16.0;
	if (roll < 255.0) return 0.0;
	if (roll < 265.0) return 1.0;
	if (roll < 269.0) return 2.0;
	if (roll < 270.0) return 3.0;
	if (roll < 355.0) return 4.0;
	if (roll < 365.0) return 5.0;
	if (roll < 369.0) return 6.0;
	if (roll < 370.0) return 7.0;
	if (roll < 455.0) return 8.0;
	if (roll < 465.0) return 9.0;
	if (roll < 469.0) return 10.0;
	if (roll < 470.0) return 11.0;
	if (roll < 555.0) return 12.0;
	if (roll < 565.0) return 13.0;
	if (roll < 569.0) return 14.0;
	return 15.0;
}

void vertex() {
	war3_fog_world_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

void fragment() {
	vec2 cell = floor(UV + vec2(0.0001));
	float variation = weighted_variation(cell);
	float tile_index = variation >= 15.5 ? 15.0 : 16.0 + variation;
	float local_index = tile_index >= 16.0
		? tile_index - 16.0
		: tile_index;
	vec2 atlas_cell = vec2(
		mod(local_index, 4.0) + (tile_index >= 16.0 ? 4.0 : 0.0),
		floor(local_index / 4.0));
	vec2 warp = vec2(
		value_noise(UV * 0.18, 41.3),
		value_noise(UV * 0.18, 73.9));
	vec2 sample_uv = UV + (warp - vec2(0.5)) * (macro_uv_warp * 2.0);
	vec2 local_uv = mix(vec2(0.012), vec2(0.988), fract(sample_uv));
	vec2 atlas_uv = (atlas_cell + local_uv) / vec2(8.0, 4.0);
	vec4 sampled = texture(terrain_atlas, atlas_uv);
	float tint = mix(
		1.0 - macro_tint,
		1.0 + macro_tint,
		value_noise(UV * 0.11, 101.7));
	ALBEDO = sampled.rgb * color_tint * tint * war3_fog_light();
	ROUGHNESS = 0.96;
}
