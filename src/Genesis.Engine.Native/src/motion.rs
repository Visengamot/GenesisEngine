#[repr(C)]
struct Motion {
    x: f32,
    y: f32,
    vx: f32,
    vy: f32,
    speed: f32,
    size: f32,
    metabolism: f32,
    energy: f32,
}

fn reflect(mut at: f32, max: f32, direction: &mut f32) -> f32 {
    if max <= 0.0 {
        *direction = 0.0;
        return 0.0;
    }
    while at < 0.0 || at > max {
        if at < 0.0 {
            at = -at;
            *direction = direction.abs();
        }
        if at > max {
            at = 2.0 * max - at;
            *direction = -direction.abs();
        }
    }
    at
}

/// # Safety
/// Buffers are non-overlapping, valid for their lengths, and exclusively mutable
/// where applicable. The dimensions, positions, dt and genome are validated by C#.
#[unsafe(no_mangle)]
unsafe extern "C" fn genesis_move_and_feed(
    width: usize, height: usize, heights: *const f32, food: *mut f32,
    dt: f32, deep_water: f32, mountain: f32, state: *mut Motion,
) {
    let heights = unsafe { std::slice::from_raw_parts(heights, width * height) };
    let food = unsafe { std::slice::from_raw_parts_mut(food, width * height) };
    let state = unsafe { &mut *state };
    let index = |x: f32, y: f32| (x as i32, y as i32);
    let elevation = |x: f32, y: f32| {
        let (x, y) = index(x, y);
        if x < 0 || y < 0 || x as usize >= width || y as usize >= height {
            0.0 // TerrainMap.Get returns zero outside the map, including reflection probes.
        } else {
            heights[y as usize * width + x as usize]
        }
    };
    let blocked = |x, y| { let h = elevation(x, y); h <= deep_water || h >= mountain };
    let (mut x, mut y) = (state.x, state.y);
    let (mut vx, mut vy) = (state.vx, state.vy);
    let length = (vx * vx + vy * vy).sqrt();
    if length > 1.0 {
        let scale = 1.0 / length;
        vx *= scale;
        vy *= scale;
    }
    let (max_x, max_y) = ((width - 1) as f32, (height - 1) as f32);
    let speed = state.speed * 4.0 * dt;
    let (mut preview_x, mut preview_y) = (vx, vy);
    let tx = reflect(x + vx * speed, max_x, &mut preview_x);
    let ty = reflect(y + vy * speed, max_y, &mut preview_y);
    let slope = (elevation(tx, ty) - elevation(x, y)).abs();
    let distance = speed / (1.0 + slope);
    let steps = ((vx.abs().max(vy.abs()) as f64) * distance as f64 * 4.0).ceil().max(1.0) as usize;
    let stride = distance / steps as f32;
    let (mut dx, mut dy) = (vx * stride, vy * stride);
    for _ in 0..steps {
        let mut nx = reflect(x + dx, max_x, &mut dx);
        let mut ny = reflect(y + dy, max_y, &mut dy);
        if nx as i32 != x as i32 && blocked(nx, y) {
            let edge = if dx > 0.0 { nx.trunc() } else { x.trunc() };
            nx = 2.0 * edge - nx;
            if dx > 0.0 { nx = nx.next_down(); }
            dx = -dx;
        }
        if ny as i32 != y as i32 && blocked(x, ny) {
            let edge = if dy > 0.0 { ny.trunc() } else { y.trunc() };
            ny = 2.0 * edge - ny;
            if dy > 0.0 { ny = ny.next_down(); }
            dy = -dy;
        }
        nx = reflect(nx, max_x, &mut dx);
        ny = reflect(ny, max_y, &mut dy);
        if blocked(nx, ny) && !blocked(x, y) {
            nx = x; ny = y; dx = -dx; dy = -dy;
        }
        x = nx; y = ny;
    }
    state.x = x;
    state.y = y;
    state.vx = vx.abs().copysign(dx);
    state.vy = vy.abs().copysign(dy);
    // Match FoodMap.Consume's signed linear index, rather than saturating a negative cast.
    let (ix, iy) = index(x, y);
    let amount = &mut food[(iy as isize * width as isize + ix as isize) as usize];
    let eaten = amount.min(0.35 * state.size * dt);
    *amount -= eaten;
    state.energy = (state.energy + eaten - dt * (0.12 * state.metabolism * state.size
        + 0.025 * state.speed * length * (1.0 + slope))).min(3.0);
}
