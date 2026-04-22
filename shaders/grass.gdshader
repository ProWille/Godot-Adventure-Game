shader_type spatial;

render_mode world_vertex_coords, cull_disabled;

group_uniforms Colors;
uniform vec3 grass_color : source_color = vec3(0.2, 0.6, 0.15);
uniform vec3 tip_color : source_color = vec3(0.4, 0.8, 0.2);
group_uniforms;

group_uniforms Size;
uniform float grass_height : hint_range(0.1, 3.0) = 1.0;
uniform float height_variation : hint_range(0.0, 1.0) = 0.3;
uniform float blade_width : hint_range(0.01, 0.2) = 0.05;
group_uniforms;

group_uniforms Noise;
uniform sampler2D noise_texture : repeat_enable;
uniform float noise_scale : hint_range(0.01, 1.0) = 0.1;
uniform float wind_strength : hint_range(0.0, 1.0) = 0.2;
group_uniforms;

varying float v_heightRatio;

void vertex()
{
    float noiseVal = texture(noise_texture, VERTEX.xz * noise_scale).r;
    float heightMod = 1.0 + (noiseVal - 0.5) * 2.0 * height_variation;
    float actualHeight = grass_height * heightMod;
    
    VERTEX.y *= actualHeight;
    
    float windTime = TIME * 0.5;
    float wind = sin(windTime + VERTEX.x * 0.5 + VERTEX.z * 0.3) * wind_strength;
    VERTEX.x += wind * UV.y * actualHeight;
    VERTEX.z += wind * UV.y * actualHeight * 0.5;
    
    v_heightRatio = UV.y;
}

void fragment()
{
    float noiseVal = texture(noise_texture, UV * 3.0 + vec2(TIME * 0.1)).r;
    
    vec3 baseCol = mix(grass_color, tip_color, v_heightRatio);
    baseCol += (noiseVal - 0.5) * 0.15;
    
    float ao = smoothstep(0.0, 0.3, v_heightRatio);
    baseCol *= 0.7 + ao * 0.3;
    
    ALBEDO = baseCol;
    ROUGHNESS = 0.85;
    METALLIC = 0.0;
}
