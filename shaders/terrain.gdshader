shader_type spatial;

uniform sampler2D water_texture : source_color;
uniform sampler2D sand_texture : source_color;
uniform sampler2D grass_texture : source_color;
uniform sampler2D forest_texture : source_color;
uniform sampler2D jungle_texture : source_color;
uniform sampler2D desert_texture : source_color;
uniform sampler2D savanna_texture : source_color;
uniform sampler2D tundra_texture : source_color;
uniform sampler2D rock_texture : source_color;
uniform sampler2D snow_texture : source_color;

uniform float texture_scale = 10.0;
uniform bool use_textures = false;

group_uniforms BiomeColors;
uniform vec3 ocean_color : source_color = vec3(0.0, 0.2, 0.8);
uniform vec3 grassland_color : source_color = vec3(0.4, 0.8, 0.2);
uniform vec3 forest_color : source_color = vec3(0.2, 0.6, 0.1);
uniform vec3 jungle_color : source_color = vec3(0.1, 0.5, 0.1);
uniform vec3 desert_color : source_color = vec3(0.9, 0.8, 0.5);
uniform vec3 savanna_color : source_color = vec3(0.8, 0.7, 0.4);
uniform vec3 tundra_color : source_color = vec3(0.8, 0.9, 1.0);
uniform vec3 mountain_color : source_color = vec3(0.7, 0.7, 0.8);
group_uniforms;

varying float v_biome;

void vertex() {
	v_biome = COLOR.r * 8.0;
}

void fragment() {
	vec2 uv_scaled = UV * texture_scale;
	
	vec3 color;
	int biome = int(v_biome + 0.5);
	
	if (biome == 0) {
		color = use_textures ? texture(water_texture, uv_scaled).rgb : ocean_color;
	} else if (biome == 1) {
		color = use_textures ? texture(grass_texture, uv_scaled).rgb : grassland_color;
	} else if (biome == 2) {
		color = use_textures ? texture(forest_texture, uv_scaled).rgb : forest_color;
	} else if (biome == 3) {
		color = use_textures ? texture(jungle_texture, uv_scaled).rgb : jungle_color;
	} else if (biome == 4) {
		color = use_textures ? texture(desert_texture, uv_scaled).rgb : desert_color;
	} else if (biome == 5) {
		color = use_textures ? texture(savanna_texture, uv_scaled).rgb : savanna_color;
	} else if (biome == 6) {
		color = use_textures ? texture(tundra_texture, uv_scaled).rgb : tundra_color;
	} else if (biome == 7) {
		color = use_textures ? texture(rock_texture, uv_scaled).rgb : mountain_color;
	} else {
		color = ocean_color;
	}
	
	ALBEDO = color;
}
