shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_disabled;

// UV2.x packs four 4-bit atlas masks (R/G/B/A layers). UV2.y stores the
// deterministic full-tile variation. The visual control grid is independent
// from gameplay cells and contributes one material identity at each corner.
uniform sampler2D terrain_r : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D terrain_g : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D terrain_b : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D terrain_a : source_color, filter_linear_mipmap_anisotropic, repeat_disable;

float layer_mask(float packed_masks, float divisor) {
	return floor(mod(floor(packed_masks / divisor), 16.0) + 0.001);
}

vec4 atlas_sample(
		sampler2D terrain_atlas,
		vec2 local_uv,
		float mask,
		float full_variation) {
	// Classic extended terrain atlases contain 8x4 tiles of 64 pixels.
	// Indices 0..15 are corner transition masks; 16..31 are full variants.
	// Raw variation 16 deliberately selects transition tile 15, matching the
	// classic/HiveWE extended-tile convention instead of wrapping to variant 0.
	float tile_index = mask;
	if (mask >= 14.5) {
		tile_index = full_variation >= 15.5
			? 15.0
			: 16.0 + full_variation;
	}
	float local_index = tile_index >= 16.0
		? tile_index - 16.0
		: tile_index;
	vec2 atlas_cell = vec2(
		mod(local_index, 4.0) + (tile_index >= 16.0 ? 4.0 : 0.0),
		floor(local_index / 4.0));
	vec2 inset_uv = mix(vec2(0.012), vec2(0.988), local_uv);
	return texture(terrain_atlas, (atlas_cell + inset_uv) / vec2(8.0, 4.0));
}

vec4 alpha_over(vec4 under, vec4 over) {
	float alpha = over.a + under.a * (1.0 - over.a);
	vec3 rgb = mix(under.rgb, over.rgb, over.a);
	return vec4(rgb, alpha);
}

void fragment() {
	float packed = floor(UV2.x + 0.5);
	float variation = floor(UV2.y + 0.5);
	float mask_r = layer_mask(packed, 1.0);
	float mask_g = layer_mask(packed, 16.0);
	float mask_b = layer_mask(packed, 256.0);
	float mask_a = layer_mask(packed, 4096.0);
	vec2 grid_uv = fract(UV + vec2(0.0001));
	// W3E/HiveWE names tilepoint rows bottom-to-top while atlas rows are
	// stored top-to-bottom, so the transition tile's V axis is inverted.
	vec2 local_uv = vec2(grid_uv.x, 1.0 - grid_uv.y);
	vec4 result = vec4(0.0);
	if (mask_r > 0.5) {
		result = alpha_over(
			result, atlas_sample(terrain_r, local_uv, mask_r, variation));
	}
	if (mask_g > 0.5) {
		result = alpha_over(
			result, atlas_sample(terrain_g, local_uv, mask_g, variation));
	}
	if (mask_b > 0.5) {
		result = alpha_over(
			result, atlas_sample(terrain_b, local_uv, mask_b, variation));
	}
	if (mask_a > 0.5) {
		result = alpha_over(
			result, atlas_sample(terrain_a, local_uv, mask_a, variation));
	}
	ALBEDO = result.rgb;
	ROUGHNESS = 0.95;
}
