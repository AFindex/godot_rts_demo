shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_schlick_ggx;

uniform sampler2D water_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec3 shallow_tint : source_color = vec3(0.78, 0.94, 1.0);
uniform float depth_tint = 0.0;

void fragment() {
	vec2 flow_a = UV * 0.72 + vec2(TIME * 0.018, TIME * 0.009);
	vec2 flow_b = UV * 0.91 + vec2(-TIME * 0.011, TIME * 0.016);
	vec3 a = texture(water_texture, flow_a).rgb;
	vec3 b = texture(water_texture, flow_b).rgb;
	vec3 water = mix(a, b, 0.42) * shallow_tint;
	water = mix(water, vec3(0.07, 0.18, 0.32), depth_tint);
	ALBEDO = water;
	ROUGHNESS = 0.34;
	METALLIC = 0.08;
}
