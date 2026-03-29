shader_type spatial;

uniform sampler2D sand_texture : source_color;
uniform sampler2D grass_texture : source_color;
uniform sampler2D forest_texture : source_color;
uniform sampler2D rock_texture : source_color;
uniform sampler2D snow_texture : source_color;
uniform sampler2D water_texture : source_color;

uniform float texture_scale = 10.0;
uniform bool use_textures = false;

uniform float water_blue_threshold = 0.7;
uniform float water_red_limit = 0.4;
uniform float water_green_limit = 0.4;
uniform float sand_red_threshold = 0.8;
uniform float sand_green_limit = 0.4;
uniform float forest_blue_threshold = 0.6;
uniform float forest_green_min = 0.3;
uniform float grass_green_threshold = 0.6;
uniform float grass_blue_limit = 0.4;
uniform float snow_red_threshold = 0.5;
uniform float snow_blue_threshold = 0.5;
uniform float rock_red_threshold = 0.5;
uniform float rock_green_threshold = 0.5;

varying vec3 vertex_color;

void vertex() {
	vertex_color = COLOR.rgb;
}

void fragment() {
	vec2 uv_scaled = UV * texture_scale;
	
	vec3 biome_color = vertex_color;
	
	vec3 color;
	float max_comp = max(biome_color.r, max(biome_color.g, biome_color.b));
	vec3 normalized = max_comp > 0.0 ? biome_color / max_comp : vec3(0.0);
	
	vec3 sand = use_textures ? texture(sand_texture, uv_scaled).rgb : vec3(0.9, 0.76, 0.5);
	vec3 grass = use_textures ? texture(grass_texture, uv_scaled).rgb : vec3(0.4, 0.7, 0.2);
	vec3 forest = use_textures ? texture(forest_texture, uv_scaled).rgb : vec3(0.15, 0.45, 0.1);
	vec3 rock = use_textures ? texture(rock_texture, uv_scaled).rgb : vec3(0.5, 0.5, 0.55);
	vec3 snow = use_textures ? texture(snow_texture, uv_scaled).rgb : vec3(0.9, 0.92, 0.95);
	vec3 water = use_textures ? texture(water_texture, uv_scaled).rgb : vec3(0.1, 0.4, 0.7);
	
	if (normalized.b > water_blue_threshold && normalized.r < water_red_limit && normalized.g < water_green_limit) {
		color = water;
	} else if (normalized.r > sand_red_threshold && normalized.g < sand_green_limit) {
		color = sand;
	} else if (normalized.b > forest_blue_threshold && normalized.g > forest_green_min) {
		color = forest;
	} else if (normalized.g > grass_green_threshold && normalized.b < grass_blue_limit) {
		color = grass;
	} else if (normalized.r > snow_red_threshold && normalized.b > snow_blue_threshold) {
		color = snow;
	} else if (normalized.r > rock_red_threshold && normalized.g > rock_green_threshold) {
		color = rock;
	} else {
		color = biome_color;
	}
	
	ALBEDO = color;
}
