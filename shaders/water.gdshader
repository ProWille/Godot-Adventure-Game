shader_type spatial;

render_mode world_vertex_coords, cull_disabled, depth_draw_always;

uniform sampler2D screen : hint_screen_texture, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D DEPTH_TEXTURE : hint_depth_texture, filter_linear_mipmap_anisotropic, repeat_disable;

varying vec2 position;
varying vec3 wposition;

group_uniforms Colors;
uniform vec3 absorption_color : source_color = vec3(0.0, 0.1, 0.3);
uniform float absorption_multiplier = 1.0;
group_uniforms;

group_uniforms Material;
uniform float air_ior = 1.0;
uniform float water_ior = 1.33;
group_uniforms;

group_uniforms TextureScroll;
uniform vec2 scroll_speed_1 = vec2(0.02, 0.0);
uniform vec2 scroll_speed_2 = vec2(0.0, 0.02);
uniform float scroll_mix : hint_range(0.0, 1.0, 0.1) = 0.5;
uniform vec2 texture_scale = vec2(0.1);
uniform sampler2D normal_map_1 : filter_linear_mipmap_anisotropic, hint_normal;
uniform sampler2D normal_map_2 : filter_linear_mipmap_anisotropic, hint_normal;
uniform float normal_strength : hint_range(0.0, 5.0, 0.01) = 1.0;
uniform sampler2D height_map_1 : filter_linear_mipmap_anisotropic;
uniform sampler2D height_map_2 : filter_linear_mipmap_anisotropic;
uniform float height_strength : hint_range(0.0, 5.0, 0.01) = 0.12;
uniform sampler2D edge_map_1 : filter_linear_mipmap_anisotropic;
uniform sampler2D edge_map_2 : filter_linear_mipmap_anisotropic;
group_uniforms;

group_uniforms Refraction;
uniform int refraction_mode : hint_enum("None", "Simple", "Traced") = 1;
uniform float simple_refraction_strength : hint_range(0.0, 1.0, 0.01) = 0.1;
uniform float traced_refraction_distance = 50.0;
uniform int traced_refraction_steps : hint_range(64, 1024, 16) = 512;
group_uniforms;

group_uniforms Edge;
uniform float edge_distance : hint_range(0.01, 0.5, 0.01) = 0.1;
uniform bool fade_instead_of_foam = false;
group_uniforms;

group_uniforms Reflections;
uniform int reflection_mode : hint_enum("None", "Simple", "Traced") = 2;
uniform float traced_reflection_distance = 50.0;
uniform int traced_reflection_steps : hint_range(64, 1024, 16) = 512;
uniform float reflection_fade_distance : hint_range(0.01, 0.5, 0.01) = 0.05;
group_uniforms;

float fresnel_schlick(float ior1, float ior2, vec3 view, vec3 normal)
{
	float incident = dot(view, normal);
	float r = ((ior2 - ior1) / (ior2 + ior1));
	float reflectance = r * r;
	float fresnel_incident = reflectance + (1.0 - reflectance) * pow(1.0 - cos(incident), 5.0);
	return clamp(fresnel_incident / incident, 0.0, 1.0);
}

void vertex()
{
	position = VERTEX.xz;
	UV = VERTEX.xz * texture_scale + (scroll_speed_1 * TIME);
	UV2 = VERTEX.xz * texture_scale + (scroll_speed_2 * TIME);

	float height = mix(texture(height_map_1, UV), texture(height_map_2, UV2), scroll_mix).x;
	VERTEX.y += (height - 0.5) * height_strength;
	wposition = VERTEX;
}

float get_linear_depth(float nonlinear_depth, mat4 inv_projection)
{
	#if CURRENT_RENDERER == RENDERER_COMPATIBILITY
		nonlinear_depth = nonlinear_depth * 2.0 - 1.0;
	#endif
	return 1.0 / (nonlinear_depth * inv_projection[2].w + inv_projection[3].w);
}

float get_nonlinear_depth(float linear_depth, mat4 inv_projection)
{
	#if CURRENT_RENDERER == RENDERER_COMPATIBILITY
		linear_depth = linear_depth * 0.5 + 0.5;
	#endif
	return (1.0 / linear_depth - inv_projection[3].w) / inv_projection[2].w;
}

vec2 view_to_uv(vec3 view_pos, mat4 proj)
{
	vec4 clip = proj * vec4(view_pos, 1.0);
	return (clip.xy / clip.w) * 0.5 + 0.5;
}

float fade_edge(vec2 uv, float size)
{
	vec2 fade = clamp(abs(uv - 0.5) * 2.0, 0.0, size) / size;
	return 1.0 - fade.x * fade.y;
}

float snells_window(vec3 normal, vec3 view_dir, float ior)
{
	float cos_theta = dot(normal, view_dir);
	return step(sqrt(1.0 - cos_theta * cos_theta) * ior, 1.0);
}

void sample_refraction(vec3 normal, vec3 vertex_pos, vec2 normal_map, bool reversed, mat4 proj, 
                      vec3 tangent, vec3 binormal, vec2 screen_uv, vec2 viewport_size, inout vec3 result)
{
	vec3 view_dir = normalize(-vertex_pos);
	mat4 inv_proj = inverse(proj);

	if (refraction_mode == 2)
	{	
		vec3 refracted = refract(view_dir, normal, reversed ? water_ior / air_ior : air_ior / water_ior);
		vec3 pos = vertex_pos;
		float dist = 0.0;
		vec2 uv;
		
		for (int i = 0; i < traced_refraction_steps; i++)
		{
			float t = float(i + 1) / float(traced_refraction_steps);
			float step_dist = t * t * traced_refraction_distance;
			pos += refracted * step_dist;
			dist += step_dist;
			
			uv = view_to_uv(pos, proj);
			float test_depth = get_linear_depth(texture(DEPTH_TEXTURE, uv).r, inv_proj);
			
			if (test_depth < -pos.z)
				break;
		}

		vec3 absorption = reversed ? vec3(1.0) : pow(absorption_color, vec3(dist * absorption_multiplier));
		result = absorption * texture(screen, uv).rgb;
	}
	else if (refraction_mode == 1)
	{
		float screen_depth = get_linear_depth(texture(DEPTH_TEXTURE, screen_uv).r, inv_proj);
		float self_depth = -vertex_pos.z;
		float depth_diff = screen_depth - self_depth;
		
		float aspect = viewport_size.y / viewport_size.x;
		vec2 offset = (normal_map - 0.5) * vec2(aspect, 1.0) * normal_strength;
		vec2 refracted_uv = screen_uv + offset * simple_refraction_strength * depth_diff / screen_depth;
		
		float new_depth = get_linear_depth(texture(DEPTH_TEXTURE, refracted_uv).r, inv_proj);
		if (new_depth < self_depth)
		{
			depth_diff = new_depth - self_depth;
		}
		
		vec3 absorption = reversed ? vec3(1.0) : pow(absorption_color, vec3(depth_diff * absorption_multiplier));
		result = (new_depth < self_depth ? texture(screen, screen_uv).rgb : texture(screen, refracted_uv).rgb) * absorption;
	}
	else
	{
		float screen_depth = get_linear_depth(texture(DEPTH_TEXTURE, screen_uv).r, inv_proj);
		float self_depth = -vertex_pos.z;
		float depth_diff = screen_depth - self_depth;
		vec3 absorption = reversed ? vec3(1.0) : pow(absorption_color, vec3(depth_diff * absorption_multiplier));
		result = texture(screen, screen_uv).rgb * absorption;
	}
}

void sample_reflection(vec3 normal, vec3 vertex_pos, mat4 proj, inout vec3 emission, inout float specular)
{
	vec3 view_dir = normalize(-vertex_pos);
	mat4 inv_proj = inverse(proj);
	vec3 reflected = -reflect(view_dir, normal);

	if (reflection_mode == 2)
	{
		vec3 pos = vertex_pos;
		vec2 uv;
		
		for (int i = 0; i < traced_reflection_steps; i++)
		{
			float t = float(i + 1) / float(traced_reflection_steps);
			float step_dist = t * t * traced_reflection_distance;
			pos += reflected * step_dist;
			
			uv = view_to_uv(pos, proj);
			
			if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
				break;
				
			float test_depth = get_linear_depth(texture(DEPTH_TEXTURE, uv).r, inv_proj);
			if (test_depth < -pos.z)
				break;
		}
		
		if (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0 && -pos.z < traced_reflection_distance * 0.99)
		{
			float fade = fade_edge(uv, reflection_fade_distance);
			specular *= 1.0 - fade;
			emission += texture(screen, uv).rgb * fresnel_schlick(air_ior, water_ior, view_dir, normal) * fade;
		}
	}
	else if (reflection_mode == 1)
	{
		if (reflected.z < 0.0)
		{
			vec2 uv = view_to_uv(reflected, proj);
			if (texture(DEPTH_TEXTURE, uv).r > 0.0)
			{
				float fade = fade_edge(uv, reflection_fade_distance);
				specular *= 1.0 - fade;
				emission += texture(screen, uv).rgb * fresnel_schlick(air_ior, water_ior, view_dir, normal) * fade;
			}
		}
	}
}

void fragment()
{
	vec3 original_normal = NORMAL;

	vec2 normal_map = mix(texture(normal_map_1, UV), texture(normal_map_2, UV2), scroll_mix).xy;
	NORMAL += TANGENT * (normal_map.x - 0.5) * normal_strength;
	NORMAL += BINORMAL * (normal_map.y - 0.5) * normal_strength;

	ROUGHNESS = 0.05;
	METALLIC = 0.0;
	ALBEDO = vec3(0.0);

	if (FRONT_FACING)
	{
		float fresnel = clamp(fresnel_schlick(air_ior, water_ior, VIEW, NORMAL), 0.0, 1.0);

		float screen_depth = get_linear_depth(texture(DEPTH_TEXTURE, SCREEN_UV).r, INV_PROJECTION_MATRIX);
		float self_depth = -VERTEX.z;
		float depth_diff = screen_depth - self_depth;

		sample_refraction(NORMAL, VERTEX, normal_map, false, PROJECTION_MATRIX, TANGENT, BINORMAL, SCREEN_UV, VIEWPORT_SIZE, EMISSION);
		EMISSION *= 1.0 - fresnel;
		sample_reflection(NORMAL, VERTEX, PROJECTION_MATRIX, EMISSION, SPECULAR);

		float edge_ratio = depth_diff * dot(normalize(NORMAL), normalize(-VERTEX)) / VIEW.z;
		if (edge_ratio < edge_distance)
		{
			edge_ratio /= edge_distance;
			if (fade_instead_of_foam)
			{
				ALPHA = edge_ratio;
			}
			else
			{
				float edge_texture = mix(texture(edge_map_1, UV), texture(edge_map_2, UV2), scroll_mix).r;
				if (edge_texture > edge_ratio)
				{
					ALBEDO = vec3(1.0);
					ROUGHNESS = 1.0;
					METALLIC = 1.0;
					EMISSION = vec3(0.0);
					NORMAL = original_normal;
				}
			}
		}
		else
		{
			ALPHA = 1.0;
		}
	}
	else
	{
		float window = dot(refract(VIEW, NORMAL, water_ior / air_ior), -NORMAL);
		if (window >= 1.0)
		{
			SPECULAR = 0.0;
			sample_refraction(NORMAL, VERTEX, normal_map, true, PROJECTION_MATRIX, TANGENT, BINORMAL, SCREEN_UV, VIEWPORT_SIZE, EMISSION);
		}
		else if (window <= 0.0)
		{
			sample_reflection(NORMAL, VERTEX, PROJECTION_MATRIX, EMISSION, SPECULAR);
		}
		else
		{
			SPECULAR *= clamp(1.0 - window, 0.0, 1.0);
			sample_refraction(NORMAL, VERTEX, normal_map, true, PROJECTION_MATRIX, TANGENT, BINORMAL, SCREEN_UV, VIEWPORT_SIZE, EMISSION);
			EMISSION *= window;
			sample_reflection(NORMAL, VERTEX, PROJECTION_MATRIX, EMISSION, SPECULAR);
		}
	}
	ALPHA = clamp(ALPHA, 0.0, 1.0);
}
