shader_type spatial;

render_mode world_vertex_coords;

uniform sampler2D normal_map_1 : repeat_enable;
uniform sampler2D normal_map_2 : repeat_enable;
uniform vec2 scroll_direction = vec2(-0.2, 1.0);
uniform float scroll_speed : hint_range(0.0, 5.0) = 1.0;
uniform float noise_scale : hint_range(0.0001, 0.1) = 0.001;
uniform float normal_strength = 0.5;

uniform float alpha : hint_range(0.0, 1.0) = 0.65;
uniform float depth_scaler : hint_range(0.01, 1.0) = 0.1;

group_uniforms Colors;
uniform vec3 surface_color : source_color = vec3(0.0, 0.5, 0.8);
uniform vec3 deep_color : source_color = vec3(0.0, 0.1, 0.3);
uniform float deep_color_offset = 0.0;
group_uniforms;

group_uniforms Fresnel;
uniform vec3 fresnel_color : source_color = vec3(0.5, 0.7, 0.9);
group_uniforms;

group_uniforms Edge;
uniform vec3 edge_color : source_color = vec3(1.0);
uniform float edge_threshold : hint_range(0.0, 1.0) = 0.1;
uniform sampler2D edge_noise : repeat_enable;
uniform float edge_wind_scale : hint_range(0.0, 5.0) = 1.0;
group_uniforms;

uniform sampler2D depth_texture : hint_depth_texture;

varying vec2 worldXZ;

float fresnel_schlick(vec3 viewDir, vec3 normal)
{
    const float F0 = 0.02;
    float cosTheta = clamp(dot(normalize(viewDir), normalize(normal)), 0.0, 1.0);
    return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
}

void vertex()
{
    worldXZ = VERTEX.xz;
    float waveHeight = (texture(normal_map_1, (scroll_direction * scroll_speed * TIME + VERTEX.xz) * 0.01).r - 0.5);
    VERTEX.y += edge_wind_scale * 0.5 * waveHeight;
}

void fragment()
{
    vec2 offset = scroll_direction * TIME * scroll_speed;
    
    NORMAL_MAP = mix(
        texture(normal_map_1, (worldXZ + offset) * noise_scale),
        texture(normal_map_2, (worldXZ + offset) * noise_scale),
        0.5
    ).rgb * normal_strength;

    vec3 albedo = surface_color;
    albedo = mix(albedo, fresnel_color, fresnel_schlick(VIEW, NORMAL));

    float depth = texture(depth_texture, SCREEN_UV).x;
    vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, depth);
    vec4 worldPos = INV_VIEW_MATRIX * INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
    vec3 worldPosition = worldPos.xyz / worldPos.w;
    float nodeY = NODE_POSITION_WORLD.y;
    float dist = (nodeY - worldPosition.y) * depth_scaler;
    
    float edgeBlend = step(dist, edge_threshold);
    float edgeNoise = texture(edge_noise, (-scroll_direction * TIME + worldXZ) * 0.02).x;
    float edgeAmount = clamp(edgeNoise + 0.2, 0.0, 1.0);
    albedo = mix(albedo, edge_color, edgeAmount * edgeBlend);

    float deepAmount = clamp(dist * (deep_color_offset + 1.0) * 0.6, 0.0, 1.0);
    deepAmount = pow(deepAmount, 0.85);
    albedo = mix(albedo, deep_color, deepAmount);

    ALBEDO = albedo;
    ROUGHNESS = 0.02;
    ALPHA = alpha;
}
