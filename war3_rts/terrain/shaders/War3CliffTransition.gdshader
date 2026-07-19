shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_disabled;

#include "res://war3_rts/terrain/shaders/War3Fog.gdshaderinc"

uniform sampler2D cliff_atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec3 color_tint : source_color = vec3(0.93, 0.93, 0.93);
uniform bool use_fine_height = false;
uniform sampler2D fine_height_map : filter_linear, repeat_disable;
uniform vec2 fine_height_origin = vec2(0.0);
uniform vec2 fine_height_map_size = vec2(1.0);
uniform float fine_height_cell_size = 1.0;

void vertex() {
	if (use_fine_height) {
		vec3 world_vertex = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
		vec2 point = (world_vertex.xz - fine_height_origin) /
			max(fine_height_cell_size, 0.0001);
		vec2 map_uv = (point + vec2(0.5)) / fine_height_map_size;
		float world_offset = textureLod(fine_height_map, map_uv, 0.0).r;
		VERTEX += (inverse(MODEL_MATRIX) *
			vec4(0.0, world_offset, 0.0, 0.0)).xyz;
	}
	war3_fog_world_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

void fragment() {
	vec2 atlas_uv = clamp(UV, vec2(0.002), vec2(0.998));
	vec4 sampled = texture(cliff_atlas, atlas_uv);
	ALBEDO = sampled.rgb * color_tint * war3_fog_light();
	ROUGHNESS = 0.92;
}
