shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_disabled;

uniform sampler2D cliff_atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec3 color_tint : source_color = vec3(0.93, 0.93, 0.93);
uniform bool use_model_atlas_uv = false;

void fragment() {
	// The exported classic cliff texture is a 2x2 atlas.  The upper-left
	// quadrant is the repeatable rock face; original cliff MDX pieces select
	// the other regions for tops and corner trims.
	vec2 local_uv = mix(vec2(0.008), vec2(0.992), fract(UV));
	vec2 atlas_uv = use_model_atlas_uv
		? clamp(UV, vec2(0.002), vec2(0.998))
		: local_uv * 0.5;
	vec4 sampled = texture(cliff_atlas, atlas_uv);
	ALBEDO = sampled.rgb * color_tint;
	ROUGHNESS = 0.92;
}
