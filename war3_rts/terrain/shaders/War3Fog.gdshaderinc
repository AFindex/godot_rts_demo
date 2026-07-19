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
	return texture(war3_fog_texture, clamp(fog_uv, vec2(0.0), vec2(1.0))).r;
}
