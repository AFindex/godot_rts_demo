shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_disabled;

uniform sampler2D cliff_atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec3 color_tint : source_color = vec3(0.93, 0.93, 0.93);
uniform float terrain_reveal_inner_height = 17.0;
uniform bool use_fine_height = false;
uniform sampler2D fine_height_map : filter_linear, repeat_disable;
uniform vec2 fine_height_origin = vec2(0.0);
uniform vec2 fine_height_map_size = vec2(1.0);
uniform float fine_height_cell_size = 1.0;

varying float terrain_reveal_alpha;
varying vec2 cliff_footprint;
varying vec4 terrain_reveal_edges;

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
	terrain_reveal_alpha = smoothstep(
		0.0, terrain_reveal_inner_height, VERTEX.z);
	cliff_footprint = clamp(
		vec2((VERTEX.x + 128.0) / 128.0, VERTEX.y / 128.0),
		vec2(0.0), vec2(1.0));
	terrain_reveal_edges = INSTANCE_CUSTOM;
}

void fragment() {
	vec2 atlas_uv = clamp(UV, vec2(0.002), vec2(0.998));
	vec4 sampled = texture(cliff_atlas, atlas_uv);
	// WarcraftRoot plus the placement basis maps raw coordinates to world as
	// (rawY, rawZ, -rawX): footprint.x is top-to-bottom and y left-to-right.
	float edge_distance = cliff_footprint.x;
	float can_reveal = terrain_reveal_edges.r;
	if (1.0 - cliff_footprint.y < edge_distance) {
		edge_distance = 1.0 - cliff_footprint.y;
		can_reveal = terrain_reveal_edges.g;
	}
	if (1.0 - cliff_footprint.x < edge_distance) {
		edge_distance = 1.0 - cliff_footprint.x;
		can_reveal = terrain_reveal_edges.b;
	}
	if (cliff_footprint.y < edge_distance) {
		can_reveal = terrain_reveal_edges.a;
	}
	ALBEDO = sampled.rgb * color_tint;
	ROUGHNESS = 0.92;
	ALPHA = sampled.a * mix(1.0, terrain_reveal_alpha, can_reveal);
}
