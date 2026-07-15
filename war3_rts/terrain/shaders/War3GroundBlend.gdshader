shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_disabled;

// Vertex COLOR stores presentation-only weights generated from neighbouring
// semantic cells. R/G/B/A are dirt, rough dirt, rock and grass respectively.
uniform sampler2D terrain_r : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D terrain_g : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D terrain_b : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D terrain_a : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float edge_noise : hint_range(0.0, 0.3) = 0.12;
uniform float blend_contrast : hint_range(0.6, 2.0) = 1.12;
uniform float macro_uv_warp : hint_range(0.0, 0.12) = 0.045;
uniform float macro_tint : hint_range(0.0, 0.12) = 0.035;

float cell_hash(vec2 cell, float salt) {
	return fract(sin(dot(cell + salt, vec2(127.1, 311.7))) * 43758.5453);
}

float weighted_variation(vec2 cell, float salt) {
	// 570 total: 0/16/0 = 85 each, then 10/4/1 and three
	// 85/10/4/1 authored groups. Coordinate hashing replaces mutable RNG.
	float roll = floor(cell_hash(cell, salt) * 570.0);
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

float blend_noise(vec2 position, float salt) {
	vec2 coarse_cell = floor(position * 3.0);
	vec2 local = fract(position * 3.0);
	local = local * local * (3.0 - 2.0 * local);
	float a = cell_hash(coarse_cell, salt);
	float b = cell_hash(coarse_cell + vec2(1.0, 0.0), salt);
	float c = cell_hash(coarse_cell + vec2(0.0, 1.0), salt);
	float d = cell_hash(coarse_cell + vec2(1.0, 1.0), salt);
	return mix(mix(a, b, local.x), mix(c, d, local.x), local.y);
}

vec2 domain_warp(vec2 uv) {
	vec2 low_frequency = vec2(
		blend_noise(uv * 0.18, 41.3),
		blend_noise(uv * 0.18, 73.9));
	return uv + (low_frequency - vec2(0.5)) * (macro_uv_warp * 2.0);
}

vec3 atlas_sample(
		sampler2D terrain_atlas,
		vec2 selection_uv,
		vec2 sample_uv,
		float salt) {
	vec2 cell = floor(selection_uv + vec2(0.0001));
	float variation = weighted_variation(cell, salt);
	float tile_index = variation >= 15.5 ? 15.0 : 16.0 + variation;
	float local_index = tile_index >= 16.0
		? tile_index - 16.0
		: tile_index;
	vec2 atlas_cell = vec2(
		mod(local_index, 4.0) + (tile_index >= 16.0 ? 4.0 : 0.0),
		floor(local_index / 4.0));
	vec2 local_uv = mix(vec2(0.012), vec2(0.988), fract(sample_uv));
	vec2 atlas_uv = (atlas_cell + local_uv) / vec2(8.0, 4.0);
	return texture(terrain_atlas, atlas_uv).rgb;
}

void fragment() {
	vec4 source_weights = max(COLOR, vec4(0.0));
	vec4 active = step(vec4(0.0005), source_weights);
	vec4 noise = vec4(
		blend_noise(UV * 0.45, 1.7),
		blend_noise(UV * 0.45, 7.1),
		blend_noise(UV * 0.45, 13.3),
		blend_noise(UV * 0.45, 23.9));
	vec4 weights = max(
		source_weights + (noise - vec4(0.5)) * edge_noise,
		vec4(0.0)) * active;
	weights = pow(weights, vec4(blend_contrast));
	weights /= max(dot(weights, vec4(1.0)), 0.0001);

	vec2 warped_uv = domain_warp(UV);
	vec3 dirt = atlas_sample(terrain_r, UV, warped_uv, 2.0);
	vec3 rough_dirt = atlas_sample(terrain_g, UV, warped_uv, 11.0);
	vec3 rock = atlas_sample(terrain_b, UV, warped_uv, 19.0);
	vec3 grass = atlas_sample(terrain_a, UV, warped_uv, 31.0);
	float tint = mix(
		1.0 - macro_tint,
		1.0 + macro_tint,
		blend_noise(UV * 0.11, 101.7));
	ALBEDO = (dirt * weights.r + rough_dirt * weights.g +
		rock * weights.b + grass * weights.a) * tint;
	ROUGHNESS = dot(weights, vec4(0.97, 0.96, 0.91, 0.95));
}
