use std::{cell::RefCell, cmp::Ordering, collections::{BinaryHeap, HashMap}};
use super::Cell;

struct Entry { node: usize, score: f32, order: u32 }
impl Ord for Entry {
    fn cmp(&self, other: &Self) -> Ordering {
        other.score.total_cmp(&self.score).then_with(|| other.order.cmp(&self.order))
    }
}
impl PartialOrd for Entry { fn partial_cmp(&self, other: &Self) -> Option<Ordering> { Some(self.cmp(other)) } }
impl PartialEq for Entry { fn eq(&self, other: &Self) -> bool { self.cmp(other) == Ordering::Equal } }
impl Eq for Entry {}

struct Node { cost: f32, previous: usize, closed: bool }
#[derive(Default)]
struct Scratch { nodes: HashMap<usize, Node>, open: BinaryHeap<Entry> }
thread_local! { static SCRATCH: RefCell<Scratch> = RefCell::new(Scratch::default()); }

/// The input buffers contain width * height elements. `route` has room for
/// `budget` indices and all output buffers are disjoint from the inputs.
#[unsafe(no_mangle)]
unsafe extern "C" fn genesis_search_work_route(
    width: usize, height: usize, terrain: *const f32, cells: *const Cell,
    sx: usize, sy: usize, tx: usize, ty: usize, budget: usize,
    route: *mut u32, length: *mut u32, expanded: *mut u32,
) -> u32 {
    unsafe { *length = 0; *expanded = 0; }
    if sx >= width || sy >= height || tx >= width || ty >= height || !(1..=512).contains(&budget) { return 3; }
    let terrain = unsafe { std::slice::from_raw_parts(terrain, width * height) };
    let cells = unsafe { std::slice::from_raw_parts(cells, width * height) };
    let passable = |i: usize| terrain[i] >= -0.15 && terrain[i] < 1.5 && cells[i].fire < 0.05;
    let (source, target) = (sy * width + sx, ty * width + tx);
    if !passable(source) || !passable(target) { return 3; }
    SCRATCH.with_borrow_mut(|scratch| {
        let Scratch { nodes, open } = scratch;
        nodes.clear(); open.clear();
        nodes.insert(source, Node { cost: 0.0, previous: source, closed: false });
        open.push(Entry { node: source, score: (tx.abs_diff(sx) + ty.abs_diff(sy)) as f32, order: 0 });
        let mut order = 1;
        let mut count = 0;
        while count < budget {
            let Some(Entry { node, .. }) = open.pop() else { break; };
            let current = nodes.get_mut(&node).unwrap();
            if current.closed { continue; }
            current.closed = true;
            let cost = current.cost;
            count += 1;
            if node == target {
                let out = unsafe { std::slice::from_raw_parts_mut(route, budget) };
                let mut n = 0;
                let mut at = target;
                loop {
                    out[n] = at as u32;
                    n += 1;
                    if at == source { break; }
                    at = nodes[&at].previous;
                }
                out[..n].reverse();
                unsafe { *length = n as u32; *expanded = count as u32; }
                return 0;
            }
            let (x, y) = (node % width, node / width);
            // Keep the managed E/S/W/N insertion order and the two separate f32 additions.
            for (nx, ny) in [(x+1,y), (x,y+1), (x.wrapping_sub(1),y), (x,y.wrapping_sub(1))] {
                if nx >= width || ny >= height { continue; }
                let next = ny * width + nx;
                if !passable(next) { continue; }
                let next_cost = cost + 1.0 + (terrain[next] - terrain[node]).abs();
                if nodes.get(&next).is_some_and(|old| old.closed || old.cost <= next_cost) { continue; }
                nodes.insert(next, Node { cost: next_cost, previous: node, closed: false });
                open.push(Entry { node: next, score: next_cost + tx.abs_diff(nx) as f32 + ty.abs_diff(ny) as f32, order });
                order += 1;
            }
        }
        unsafe { *expanded = count as u32; }
        if open.iter().any(|entry| !nodes[&entry.node].closed) { 2 } else { 1 }
    })
}
