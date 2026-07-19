uniform bool war3_fog_enabled = false;
uniform sampler2D war3_fog_texture : filter_linear, repeat_disable;
uniform vec2 war3_fog_world_origin = vec2(0.0);
uniform vec2 war3_fog_world_size = vec2(1.0);

varying vec2 war3_fog_world_xz;

float war3_fog_light() {
	if (!war3_fog_enabled) {
		return 1.0;
	}
	vec2 fog_uv = (war3_fog_world_xz - war3_fog_world_origin) /
		max(war3_fog_world_size, vec2(0.0001));
	// R8 encodes authoritative state as Hidden=0, Explored=0.5,
	// Visible=1. Decode the two transitions independently so the middle state
	// remains a clearly readable, stable band under Godot's tone mapping.
	float fog_state = texture(
		war3_fog_texture,
		clamp(fog_uv, vec2(0.0), vec2(1.0))).r;
	float explored_weight = smoothstep(0.08, 0.42, fog_state);
	float visible_weight = smoothstep(0.58, 0.92, fog_state);
	float hidden_or_explored = mix(0.025, 0.52, explored_weight);
	return mix(hidden_or_explored, 1.0, visible_weight);
}
