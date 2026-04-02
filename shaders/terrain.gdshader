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
uniform vec3 sand_color : source_color = vec3(0.9, 0.76, 0.5);
uniform vec3 snow_color : source_color = vec3(0.9, 0.92, 0.95);
group_uniforms;

varying float v_biome;
varying float v_blend;
varying float v_blendedBiome;

void vertex() {
	v_biome = COLOR.r * 10.0;
	v_blend = COLOR.g;
	v_blendedBiome = COLOR.b * 10.0;
}

vec3 get_biome_color(int biome, vec2 uv) {
    switch (biome)
    {
		case 0: return use_textures ? texture(water_texture, uv).rgb : ocean_color;
		case 1: return use_textures ? texture(grass_texture, uv).rgb : grassland_color;
		case 2: return use_textures ? texture(forest_texture, uv).rgb : forest_color;
		case 3: return use_textures ? texture(jungle_texture, uv).rgb : jungle_color;
		case 4: return use_textures ? texture(desert_texture, uv).rgb : desert_color;
		case 5: return use_textures ? texture(savanna_texture, uv).rgb : savanna_color;
		case 6: return use_textures ? texture(tundra_texture, uv).rgb : tundra_color;
		case 7: return use_textures ? texture(rock_texture, uv).rgb : mountain_color;
		case 8: return use_textures ? texture(sand_texture, uv).rgb : sand_color;
		case 9: return use_textures ? texture(snow_texture, uv).rgb : snow_color;
    }
	return ocean_color;
}

void fragment() {
	vec2 uv_scaled = UV * texture_scale;
	
	int biome = int(v_biome + 0.5);
	int blendedBiome = int(v_blendedBiome + 0.5);
	
	vec3 color1 = get_biome_color(biome, uv_scaled);
	vec3 color2 = get_biome_color(blendedBiome, uv_scaled);
	
	vec3 color = mix(color1, color2, v_blend);
	
	ALBEDO = color;
}
