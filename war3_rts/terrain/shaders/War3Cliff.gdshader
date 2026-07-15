shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_disabled;

uniform sampler2D cliff_atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D cliff_atlas_secondary : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec3 color_tint : source_color = vec3(0.93, 0.93, 0.93);
uniform bool use_model_atlas_uv = false;
uniform bool use_instance_cliff_blend = false;

varying float cliff_atlas_blend;
varying vec3 cliff_world_position;

void vertex() {
	// Exported natural-cliff meshes retain Warcraft's XY footprint in the
	// -128..0 / 0..128 range. INSTANCE_CUSTOM stores TL/TR/BR/BL weights.
	vec2 footprint = clamp(
		vec2((VERTEX.x + 128.0) / 128.0, VERTEX.y / 128.0),
		vec2(0.0), vec2(1.0));
	float bottom = mix(INSTANCE_CUSTOM.a, INSTANCE_CUSTOM.b, footprint.x);
	float top = mix(INSTANCE_CUSTOM.r, INSTANCE_CUSTOM.g, footprint.x);
	cliff_atlas_blend = use_instance_cliff_blend
		? mix(bottom, top, footprint.y)
		: 0.0;
	cliff_world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	// The exported classic cliff texture is a 2x2 atlas.  The upper-left
	// quadrant is the repeatable rock face; original cliff MDX pieces select
	// the other regions for tops and corner trims.
	vec2 local_uv = mix(vec2(0.008), vec2(0.992), fract(UV));
	vec2 atlas_uv = use_model_atlas_uv
		? clamp(UV, vec2(0.002), vec2(0.998))
		: local_uv * 0.5;
	vec4 primary = texture(cliff_atlas, atlas_uv);
	vec4 secondary = texture(cliff_atlas_secondary, atlas_uv);
	float disturbance = (
		sin(cliff_world_position.x * 5.73 + cliff_world_position.z * 2.91) +
		sin(cliff_world_position.x * 2.17 - cliff_world_position.z * 6.31)
	) * 0.045;
	float blend = smoothstep(
		0.12, 0.88, clamp(cliff_atlas_blend + disturbance, 0.0, 1.0));
	vec4 sampled = mix(primary, secondary, blend);
	ALBEDO = sampled.rgb * color_tint;
	ROUGHNESS = 0.92;
}
