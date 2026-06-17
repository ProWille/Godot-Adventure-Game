shader_type spatial;

uniform sampler2D sand_texture : source_color;
uniform sampler2D grass_texture : source_color;
uniform sampler2D forest_texture : source_color;
uniform sampler2D rock_texture : source_color;

uniform float texture_scale = 10.0;
uniform bool use_textures = false;

group_uniforms BiomeColors;
uniform vec3 sand_color : source_color = vec3(0.9, 0.76, 0.5);
uniform vec3 grass_color : source_color = vec3(0.4, 0.8, 0.2);
uniform vec3 forest_color : source_color = vec3(0.2, 0.6, 0.1);
uniform vec3 rock_color : source_color = vec3(0.7, 0.7, 0.8);
group_uniforms;

varying float v_biome;
varying float v_blend;
varying float v_blendedBiome;
varying vec2 v_world_uv;

void vertex() {
	v_biome = COLOR.r * 10.0;
	v_blend = COLOR.g;
	v_blendedBiome = COLOR.b * 10.0;
	v_world_uv = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz / texture_scale;
}

vec3 get_biome_color(int biome, vec2 uv) {
    switch (biome)
    {
		case 0: return use_textures ? texture(sand_texture, uv).rgb : sand_color;
		case 1: return use_textures ? texture(grass_texture, uv).rgb : grass_color;
		case 2: return use_textures ? texture(forest_texture, uv).rgb : forest_color;
		case 3: return use_textures ? texture(rock_texture, uv).rgb : rock_color;
    }
	return vec3(1.0, 1.0, 1.0);
}

void fragment() {
	int biome = int(v_biome + 0.5);
	int blendedBiome = int(v_blendedBiome + 0.5);
	
	vec3 color1 = get_biome_color(biome, v_world_uv);
	vec3 color2 = get_biome_color(blendedBiome, v_world_uv);
	
	vec3 color = mix(color1, color2, v_blend);
	
	ALBEDO = color;
}
