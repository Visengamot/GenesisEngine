#[repr(C)]
struct Cell {
    moisture: f32,
    temperature: f32,
    vegetation: f32,
    wood: f32,
    stone: f32,
    fire: f32,
}

#[unsafe(no_mangle)]
pub extern "C" fn genesis_abi_version() -> u32 {
    1
}

/// # Safety
/// All buffers must be aligned, disjoint and contain `width * height` initialized
/// elements, and dimensions must be positive. The mutable buffers must be
/// exclusively borrowed for this call. Inputs are finite, with cell values in
/// the same ranges as the managed simulation.
#[unsafe(no_mangle)]
unsafe extern "C" fn genesis_step_ecology(
    width: usize,
    height: usize,
    dt: f32,
    terrain: *const f32,
    food: *mut f32,
    cells: *mut Cell,
    fire: *mut f32,
) {
    let len = width * height;
    let (terrain, food, cells, fire) = unsafe {
        (
            std::slice::from_raw_parts(terrain, len),
            std::slice::from_raw_parts_mut(food, len),
            std::slice::from_raw_parts_mut(cells, len),
            std::slice::from_raw_parts_mut(fire, len),
        )
    };
    for (out, cell) in fire.iter_mut().zip(cells.iter()) {
        *out = cell.fire;
    }
    for (y, row) in cells.chunks_exact_mut(width).enumerate() {
        for (x, c) in row.iter_mut().enumerate() {
            let i = y * width + x;
            let left = if x > 0 { fire[i - 1] } else { 0.0 };
            let right = if x + 1 < width { fire[i + 1] } else { 0.0 };
            let above = if y > 0 { fire[i - width] } else { 0.0 };
            let below = if y + 1 < height { fire[i + width] } else { 0.0 };
            let adjacent = left + right + above + below;
            let elevation = terrain[i];
            let renewal = 1.0 / (1.0 + elevation.abs());
            let renewed = (food[i] + 0.03 * renewal * dt).min(1.0);
            let fertility = c.moisture * (1.0 - (c.temperature - 22.0).abs() / 35.0).clamp(0.0, 1.0) * (1.0 - c.fire);
            let burning = c.fire * dt;
            let mut next_fire = c.fire + dt * (adjacent * 0.12 * c.wood * (1.0 - c.moisture)
                + c.fire * c.wood * (1.0 - c.moisture) * 0.3 - c.fire * (0.2 + c.moisture));
            if c.wood < 0.01 { next_fire -= 0.5 * dt; }
            let normal_temperature = 22.0 - elevation * 8.0;
            let normal_moisture = (0.65 - elevation * 0.25).clamp(0.0, 1.0);
            let excess_heat = (c.temperature - normal_temperature).max(0.0);
            c.moisture = (c.moisture + dt * ((normal_moisture - c.moisture) * 0.01 - excess_heat * 0.0002 - c.fire * 0.04)).clamp(0.0, 1.0);
            c.temperature = (c.temperature + dt * ((normal_temperature - c.temperature) * 0.01 + c.fire * 4.0)).clamp(-50.0, 60.0);
            c.vegetation = (c.vegetation + dt * fertility * 0.015 - burning * 0.4).clamp(0.0, 1.0);
            c.wood = (c.wood + dt * fertility * 0.005 - burning * 0.3).clamp(0.0, 1.0);
            c.fire = next_fire.clamp(0.0, 1.0);
            food[i] = (renewed + dt * (0.025 * fertility - 0.02 * (1.0 - fertility)) - burning * 0.5).clamp(0.0, 1.0);
        }
    }
}
