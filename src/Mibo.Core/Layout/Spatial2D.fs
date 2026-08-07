namespace Mibo.Layout

open System
open System.Buffers
open System.Numerics

// ── Square grid spatial helpers ─────────────────────────────────────────

module Grid2DSpatial =

  let inline internal toIndex x y w = x + y * w

  /// Internal helpers for A* pathfinding. Not intended for direct use.
  module Internal =

    [<Struct>]
    type AStarNode = {
      Col: int
      Row: int
      Priority: float32
    }

    /// Min-heap priority queue for A* pathfinding over a pooled backing array.
    [<Struct>]
    type MinHeap = {
      mutable Items: AStarNode[]
      mutable Count: int
    }

    let inline internal create(capacity: int) : MinHeap = {
      Items = ArrayPool.Shared.Rent capacity
      Count = 0
    }

    let inline internal count(heap: MinHeap) : int = heap.Count

    let inline internal push (heap: byref<MinHeap>) (node: AStarNode) =
      if heap.Count = heap.Items.Length then
        let newArr = ArrayPool.Shared.Rent(heap.Items.Length * 2)
        Array.blit heap.Items 0 newArr 0 heap.Count
        ArrayPool.Shared.Return(heap.Items)
        heap.Items <- newArr

      heap.Items.[heap.Count] <- node
      heap.Count <- heap.Count + 1
      let mutable i = heap.Count - 1

      while i > 0 do
        let parent = (i - 1) / 2

        if heap.Items.[i].Priority < heap.Items.[parent].Priority then
          let tmp = heap.Items.[i]
          heap.Items.[i] <- heap.Items.[parent]
          heap.Items.[parent] <- tmp
          i <- parent
        else
          i <- 0

    let inline internal tryPop(heap: byref<MinHeap>) : AStarNode voption =
      if heap.Count = 0 then
        ValueNone
      else
        let result = heap.Items.[0]
        heap.Count <- heap.Count - 1

        if heap.Count > 0 then
          heap.Items.[0] <- heap.Items.[heap.Count]

          let mutable i = 0
          let mutable looping = true

          while looping do
            let left = 2 * i + 1
            let right = 2 * i + 2
            let mutable smallest = i

            if
              left < heap.Count
              && heap.Items.[left].Priority < heap.Items.[smallest].Priority
            then
              smallest <- left

            if
              right < heap.Count
              && heap.Items.[right].Priority < heap.Items.[smallest].Priority
            then
              smallest <- right

            if smallest <> i then
              let tmp = heap.Items.[i]
              heap.Items.[i] <- heap.Items.[smallest]
              heap.Items.[smallest] <- tmp
              i <- smallest
            else
              looping <- false

        ValueSome result

    let inline internal dispose(heap: byref<MinHeap>) =
      ArrayPool.Shared.Return(heap.Items)

  /// Returns the 4 cardinal (N/S/E/W) neighbors of (x, y), filtered to grid bounds.
  let inline neighbors4 x y (grid: CellGrid2D<'T>) : struct (int * int)[] =
    let struct (w, h) = struct (grid.Width, grid.Height)
    let mutable n = 0

    if x > 0 then
      n <- n + 1

    if x < w - 1 then
      n <- n + 1

    if y > 0 then
      n <- n + 1

    if y < h - 1 then
      n <- n + 1

    let result = Array.zeroCreate<struct (int * int)> n
    let mutable i = 0

    if x > 0 then
      result.[i] <- struct (x - 1, y)
      i <- i + 1

    if x < w - 1 then
      result.[i] <- struct (x + 1, y)
      i <- i + 1

    if y > 0 then
      result.[i] <- struct (x, y - 1)
      i <- i + 1

    if y < h - 1 then
      result.[i] <- struct (x, y + 1)
      i <- i + 1

    result

  /// Returns the 8 surrounding neighbors (cardinal + diagonal), filtered to grid bounds.
  let inline neighbors8 x y (grid: CellGrid2D<'T>) : struct (int * int)[] =
    let struct (w, h) = struct (grid.Width, grid.Height)
    let mutable n = 0

    for dy in -1 .. 1 do
      for dx in -1 .. 1 do
        if dx <> 0 || dy <> 0 then
          let struct (nx, ny) = struct (x + dx, y + dy)

          if nx >= 0 && nx < w && ny >= 0 && ny < h then
            n <- n + 1

    let result = Array.zeroCreate<struct (int * int)> n
    let mutable i = 0

    for dy in -1 .. 1 do
      for dx in -1 .. 1 do
        if dx <> 0 || dy <> 0 then
          let struct (nx, ny) = struct (x + dx, y + dy)

          if nx >= 0 && nx < w && ny >= 0 && ny < h then
            result.[i] <- struct (nx, ny)
            i <- i + 1

    result

  /// Manhattan distance: cost of moving in 4 directions.
  let inline distanceManhattan x1 y1 x2 y2 : int = abs(x2 - x1) + abs(y2 - y1)

  /// Chebyshev distance: cost of moving in 8 directions (diagonal = 1).
  let inline distanceChebyshev x1 y1 x2 y2 : int =
    max (abs(x2 - x1)) (abs(y2 - y1))

  /// Euclidean distance (straight-line).
  let inline distanceEuclidean x1 y1 x2 y2 : float32 =
    let dx = float32(x2 - x1)
    let dy = float32(y2 - y1)
    sqrt(dx * dx + dy * dy)

  /// Converts a world position to the nearest grid cell coordinates.
  /// Returns ValueNone if the position is outside the grid.
  let inline worldToCell
    (worldPos: Vector2)
    (grid: CellGrid2D<'T>)
    : struct (int * int) voption =
    let fx = (worldPos.X - grid.Origin.X) / grid.CellSize.X
    let fy = (worldPos.Y - grid.Origin.Y) / grid.CellSize.Y
    let cx = int(round fx)
    let cy = int(round fy)

    if cx >= 0 && cx < grid.Width && cy >= 0 && cy < grid.Height then
      ValueSome(struct (cx, cy))
    else
      ValueNone

  /// Returns all grid cells within Chebyshev distance `range` of (x, y).
  /// Includes the origin cell when range >= 0.
  let inline inRange x y range (grid: CellGrid2D<'T>) : struct (int * int)[] =
    if range < 0 then
      Array.empty
    else
      let struct (w, h) = struct (grid.Width, grid.Height)
      let x1 = max 0 (x - range)
      let x2 = min (w - 1) (x + range)
      let y1 = max 0 (y - range)
      let y2 = min (h - 1) (y + range)
      let mutable n = 0

      for cy in y1..y2 do
        for cx in x1..x2 do
          if max (abs(cx - x)) (abs(cy - y)) <= range then
            n <- n + 1

      let result = Array.zeroCreate<struct (int * int)> n
      let mutable i = 0

      for cy in y1..y2 do
        for cx in x1..x2 do
          if max (abs(cx - x)) (abs(cy - y)) <= range then
            result.[i] <- struct (cx, cy)
            i <- i + 1

      result

  /// Returns true if a straight line from (x1,y1) to (x2,y2) is clear of
  /// blocked cells. Uses Bresenham's algorithm. The start cell is not checked;
  /// the goal cell IS checked (a blocked goal means LOS is false).
  let inline lineOfSight
    x1
    y1
    x2
    y2
    ([<InlineIfLambda>] isBlocked: int -> int -> bool)
    (grid: CellGrid2D<'T>)
    : bool =
    let struct (w, h) = struct (grid.Width, grid.Height)
    let dx = abs(x2 - x1)
    let dy = -abs(y2 - y1)
    let sx = if x1 < x2 then 1 else -1
    let sy = if y1 < y2 then 1 else -1
    let mutable err = dx + dy
    let mutable cx = x1
    let mutable cy = y1
    let mutable blocked = false

    while not(cx = x2 && cy = y2) && not blocked do
      let e2 = 2 * err

      if e2 >= dy then
        err <- err + dy
        cx <- cx + sx

      if e2 <= dx then
        err <- err + dx
        cy <- cy + sy

      if cx >= 0 && cx < w && cy >= 0 && cy < h then
        if isBlocked cx cy then
          blocked <- true
      else
        blocked <- true

    not blocked

  /// Returns the visible cells along a line from (x1,y1) toward (x2,y2),
  /// stopping at the first blocked cell. The start cell is included if not blocked.
  let inline lineOfSightCells
    x1
    y1
    x2
    y2
    ([<InlineIfLambda>] isBlocked: int -> int -> bool)
    (grid: CellGrid2D<'T>)
    : struct (int * int)[] =
    let struct (w, h) = struct (grid.Width, grid.Height)
    let scratch = ArrayPool.Shared.Rent(max (abs(x2 - x1)) (abs(y2 - y1)) + 1)

    let mutable count = 0

    if x1 >= 0 && x1 < w && y1 >= 0 && y1 < h && not(isBlocked x1 y1) then
      scratch.[count] <- struct (x1, y1)
      count <- count + 1

      let dx = abs(x2 - x1)
      let dy = -abs(y2 - y1)
      let sx = if x1 < x2 then 1 else -1
      let sy = if y1 < y2 then 1 else -1
      let mutable err = dx + dy
      let mutable cx = x1
      let mutable cy = y1
      let mutable stopped = false

      while not(cx = x2 && cy = y2) && not stopped do
        let e2 = 2 * err

        if e2 >= dy then
          err <- err + dy
          cx <- cx + sx

        if e2 <= dx then
          err <- err + dx
          cy <- cy + sy

        if cx >= 0 && cx < w && cy >= 0 && cy < h then
          if isBlocked cx cy then
            stopped <- true
          else
            scratch.[count] <- struct (cx, cy)
            count <- count + 1
        else
          stopped <- true

    let result = Array.zeroCreate<struct (int * int)> count
    Array.blit scratch 0 result 0 count
    ArrayPool.Shared.Return(scratch)
    result

  /// Flood fill from (x, y) using BFS. Returns all reachable cells for which
  /// `predicate` returns true. Does not cross cells where predicate is false.
  let inline floodFill
    x
    y
    ([<InlineIfLambda>] predicate: int -> int -> bool)
    (grid: CellGrid2D<'T>)
    : struct (int * int)[] =
    let struct (w, h) = struct (grid.Width, grid.Height)

    if w = 0 || h = 0 then
      Array.empty
    elif x < 0 || x >= w || y < 0 || y >= h then
      Array.empty
    elif not(predicate x y) then
      Array.empty
    else
      let totalCells = w * h
      let visited = ArrayPool.Shared.Rent(totalCells)
      Array.Clear(visited, 0, totalCells)
      let queue = ArrayPool.Shared.Rent(totalCells)
      let mutable head = 0
      let mutable tail = 0
      queue.[tail] <- struct (x, y)
      tail <- tail + 1
      visited.[toIndex x y w] <- 1

      while head < tail do
        let struct (cx, cy) = queue.[head]
        head <- head + 1

        if cx > 0 then
          let nx = cx - 1
          let idx = toIndex nx cy w

          if visited.[idx] = 0 && predicate nx cy then
            visited.[idx] <- 1
            queue.[tail] <- struct (nx, cy)
            tail <- tail + 1

        if cx < w - 1 then
          let nx = cx + 1
          let idx = toIndex nx cy w

          if visited.[idx] = 0 && predicate nx cy then
            visited.[idx] <- 1
            queue.[tail] <- struct (nx, cy)
            tail <- tail + 1

        if cy > 0 then
          let ny = cy - 1
          let idx = toIndex cx ny w

          if visited.[idx] = 0 && predicate cx ny then
            visited.[idx] <- 1
            queue.[tail] <- struct (cx, ny)
            tail <- tail + 1

        if cy < h - 1 then
          let ny = cy + 1
          let idx = toIndex cx ny w

          if visited.[idx] = 0 && predicate cx ny then
            visited.[idx] <- 1
            queue.[tail] <- struct (cx, ny)
            tail <- tail + 1

      let result = Array.zeroCreate<struct (int * int)> tail
      Array.blit queue 0 result 0 tail
      ArrayPool.Shared.Return(queue)
      ArrayPool.Shared.Return(visited)
      result

  /// A* pathfinding on a square grid. Returns the shortest path from
  /// (startX, startY) to (goalX, goalY) as an array of coordinates, or
  /// ValueNone if no path exists.
  ///
  /// `isPassable` returns true for cells that can be walked through.
  /// `costFn` returns the movement cost between two adjacent cells.
  let inline findPath
    startX
    startY
    goalX
    goalY
    ([<InlineIfLambda>] isPassable: int -> int -> bool)
    ([<InlineIfLambda>] costFn: int -> int -> int -> int -> float32)
    (grid: CellGrid2D<'T>)
    : struct (int * int)[] voption =
    let struct (w, h) = struct (grid.Width, grid.Height)

    if
      startX < 0
      || startX >= w
      || startY < 0
      || startY >= h
      || goalX < 0
      || goalX >= w
      || goalY < 0
      || goalY >= h
    then
      ValueNone
    elif not(isPassable startX startY) || not(isPassable goalX goalY) then
      ValueNone
    elif startX = goalX && startY = goalY then
      ValueSome [| struct (startX, startY) |]
    else
      let totalCells = w * h
      let gScore = ArrayPool.Shared.Rent(totalCells)
      let parentX = ArrayPool.Shared.Rent(totalCells)
      let parentY = ArrayPool.Shared.Rent(totalCells)
      let closed = ArrayPool.Shared.Rent(totalCells)

      for i in 0 .. totalCells - 1 do
        gScore.[i] <- infinityf
        parentX.[i] <- -1
        parentY.[i] <- -1
        closed.[i] <- 0

      let inline hCost (gx: int) (gy: int) x y : float32 =
        float32(abs(gx - x) + abs(gy - y))

      gScore.[toIndex startX startY w] <- 0f
      let mutable heap = Internal.create totalCells

      Internal.push
        &heap
        ({
          Internal.Col = startX
          Internal.Row = startY
          Internal.Priority = hCost goalX goalY startX startY
        })

      let mutable found = false

      while Internal.count heap > 0 && not found do
        match Internal.tryPop &heap with
        | ValueNone -> ()
        | ValueSome current ->
          let struct (cx, cy) = struct (current.Col, current.Row)
          let idx = toIndex cx cy w

          if closed.[idx] <> 0 then
            ()
          elif cx = goalX && cy = goalY then
            found <- true
          else
            closed.[idx] <- 1
            let mutable nx = cx - 1
            let mutable ny = cy

            if nx >= 0 && closed.[toIndex nx ny w] = 0 && isPassable nx ny then
              let nIdx = toIndex nx ny w
              let tentative = gScore.[idx] + costFn cx cy nx ny

              if tentative < gScore.[nIdx] then
                gScore.[nIdx] <- tentative
                parentX.[nIdx] <- cx
                parentY.[nIdx] <- cy

                Internal.push
                  &heap
                  ({
                    Internal.Col = nx
                    Internal.Row = ny
                    Internal.Priority = tentative + hCost goalX goalY nx ny
                  })

            nx <- cx + 1
            ny <- cy

            if nx < w && closed.[toIndex nx ny w] = 0 && isPassable nx ny then
              let nIdx = toIndex nx ny w
              let tentative = gScore.[idx] + costFn cx cy nx ny

              if tentative < gScore.[nIdx] then
                gScore.[nIdx] <- tentative
                parentX.[nIdx] <- cx
                parentY.[nIdx] <- cy

                Internal.push
                  &heap
                  ({
                    Internal.Col = nx
                    Internal.Row = ny
                    Internal.Priority = tentative + hCost goalX goalY nx ny
                  })

            nx <- cx
            ny <- cy - 1

            if ny >= 0 && closed.[toIndex nx ny w] = 0 && isPassable nx ny then
              let nIdx = toIndex nx ny w
              let tentative = gScore.[idx] + costFn cx cy nx ny

              if tentative < gScore.[nIdx] then
                gScore.[nIdx] <- tentative
                parentX.[nIdx] <- cx
                parentY.[nIdx] <- cy

                Internal.push
                  &heap
                  ({
                    Internal.Col = nx
                    Internal.Row = ny
                    Internal.Priority = tentative + hCost goalX goalY nx ny
                  })

            nx <- cx
            ny <- cy + 1

            if ny < h && closed.[toIndex nx ny w] = 0 && isPassable nx ny then
              let nIdx = toIndex nx ny w
              let tentative = gScore.[idx] + costFn cx cy nx ny

              if tentative < gScore.[nIdx] then
                gScore.[nIdx] <- tentative
                parentX.[nIdx] <- cx
                parentY.[nIdx] <- cy

                Internal.push
                  &heap
                  ({
                    Internal.Col = nx
                    Internal.Row = ny
                    Internal.Priority = tentative + hCost goalX goalY nx ny
                  })

      let result =
        if found then
          let mutable n = 0
          let mutable cx = goalX
          let mutable cy = goalY

          while cx <> startX || cy <> startY do
            n <- n + 1
            let idx = toIndex cx cy w
            cx <- parentX.[idx]
            cy <- parentY.[idx]

          n <- n + 1
          let path = Array.zeroCreate<struct (int * int)> n
          let mutable i = n - 1
          cx <- goalX
          cy <- goalY

          while cx <> startX || cy <> startY do
            path.[i] <- struct (cx, cy)
            i <- i - 1
            let idx = toIndex cx cy w
            cx <- parentX.[idx]
            cy <- parentY.[idx]

          path.[0] <- struct (startX, startY)
          ValueSome path
        else
          ValueNone

      ArrayPool.Shared.Return(gScore)
      ArrayPool.Shared.Return(parentX)
      ArrayPool.Shared.Return(parentY)
      ArrayPool.Shared.Return(closed)
      Internal.dispose &heap
      result

// ── Hex grid spatial helpers ────────────────────────────────────────────

module Hex2DSpatial =

  /// Internal helpers for hex spatial operations. Not intended for direct use.
  module Internal =

    // Hex neighbor offsets for PointyTop (offset coords)
    let pointyTopEvenRow: struct (int * int)[] = [|
      struct (-1, -1)
      struct (0, -1)
      struct (-1, 0)
      struct (1, 0)
      struct (-1, 1)
      struct (0, 1)
    |]

    let pointyTopOddRow: struct (int * int)[] = [|
      struct (0, -1)
      struct (1, -1)
      struct (-1, 0)
      struct (1, 0)
      struct (0, 1)
      struct (1, 1)
    |]

    // Hex neighbor offsets for FlatTop (offset coords)
    // Even col: NW(-1,-1) N(0,-1) NE(1,-1) SE(1,0) S(0,1) SW(-1,0)
    let flatTopEvenCol: struct (int * int)[] = [|
      struct (-1, -1)
      struct (0, -1)
      struct (1, -1)
      struct (1, 0)
      struct (0, 1)
      struct (-1, 0)
    |]

    // Odd col: NW(-1,0) N(0,-1) NE(1,0) SE(1,1) S(0,1) SW(-1,1)
    let flatTopOddCol: struct (int * int)[] = [|
      struct (-1, 0)
      struct (0, -1)
      struct (1, 0)
      struct (1, 1)
      struct (0, 1)
      struct (-1, 1)
    |]

    [<Struct>]
    type AStarNode = {
      Col: int
      Row: int
      Priority: float32
    }

    /// Min-heap priority queue for hex A* pathfinding over a pooled backing array.
    [<Struct>]
    type MinHeap = {
      mutable Items: AStarNode[]
      mutable Count: int
    }

    let inline internal create(capacity: int) : MinHeap = {
      Items = ArrayPool.Shared.Rent capacity
      Count = 0
    }

    let inline internal count(heap: MinHeap) : int = heap.Count

    let inline internal push (heap: byref<MinHeap>) (node: AStarNode) =
      if heap.Count = heap.Items.Length then
        let newArr = ArrayPool.Shared.Rent(heap.Items.Length * 2)
        Array.blit heap.Items 0 newArr 0 heap.Count
        ArrayPool.Shared.Return(heap.Items)
        heap.Items <- newArr

      heap.Items.[heap.Count] <- node
      heap.Count <- heap.Count + 1
      let mutable i = heap.Count - 1

      while i > 0 do
        let parent = (i - 1) / 2

        if heap.Items.[i].Priority < heap.Items.[parent].Priority then
          let tmp = heap.Items.[i]
          heap.Items.[i] <- heap.Items.[parent]
          heap.Items.[parent] <- tmp
          i <- parent
        else
          i <- 0

    let inline internal tryPop(heap: byref<MinHeap>) : AStarNode voption =
      if heap.Count = 0 then
        ValueNone
      else
        let result = heap.Items.[0]
        heap.Count <- heap.Count - 1

        if heap.Count > 0 then
          heap.Items.[0] <- heap.Items.[heap.Count]

          let mutable i = 0
          let mutable looping = true

          while looping do
            let left = 2 * i + 1
            let right = 2 * i + 2
            let mutable smallest = i

            if
              left < heap.Count
              && heap.Items.[left].Priority < heap.Items.[smallest].Priority
            then
              smallest <- left

            if
              right < heap.Count
              && heap.Items.[right].Priority < heap.Items.[smallest].Priority
            then
              smallest <- right

            if smallest <> i then
              let tmp = heap.Items.[i]
              heap.Items.[i] <- heap.Items.[smallest]
              heap.Items.[smallest] <- tmp
              i <- smallest
            else
              looping <- false

        ValueSome result

    let inline internal dispose(heap: byref<MinHeap>) =
      ArrayPool.Shared.Return(heap.Items)

    /// Iterates hex neighbors via callback. Zero allocation.
    let inline internal forEachNeighbor
      col
      row
      w
      h
      (orientation: HexOrientation)
      ([<InlineIfLambda>] action: int -> int -> unit)
      : unit =
      let offsets =
        match orientation with
        | PointyTop -> if row % 2 = 0 then pointyTopEvenRow else pointyTopOddRow
        | FlatTop -> if col % 2 = 0 then flatTopEvenCol else flatTopOddCol

      for i in 0..5 do
        let struct (dc, dr) = offsets.[i]
        let struct (nc, nr) = struct (col + dc, row + dr)

        if nc >= 0 && nc < w && nr >= 0 && nr < h then
          action nc nr

  /// Converts offset (col, row) to cube (q, r, s) coordinates.
  let inline offsetToCube
    col
    row
    (orientation: HexOrientation)
    : struct (int * int * int) =
    match orientation with
    | PointyTop ->
      let q = col - (row - (row &&& 1)) / 2
      let r = row
      struct (q, r, -q - r)
    | FlatTop ->
      let q = col
      let r = row - (col - (col &&& 1)) / 2
      struct (q, r, -q - r)

  /// Converts cube (q, r, s) to offset (col, row) coordinates.
  let inline cubeToOffset
    q
    r
    (orientation: HexOrientation)
    : struct (int * int) =
    match orientation with
    | PointyTop ->
      let col = q + (r - (r &&& 1)) / 2
      struct (col, r)
    | FlatTop ->
      let col = q
      let row = r + (q - (q &&& 1)) / 2
      struct (col, row)

  /// Rounds fractional cube coordinates to the nearest integer hex.
  let inline cubeRound
    (fq: float32)
    (fr: float32)
    (fs: float32)
    : struct (int * int * int) =
    let mutable rq = round fq |> int
    let mutable rr = round fr |> int
    let mutable rs = round fs |> int
    let dq = abs(float32 rq - fq)
    let dr = abs(float32 rr - fr)
    let ds = abs(float32 rs - fs)

    if dq > dr && dq > ds then rq <- -rr - rs
    elif dr > ds then rr <- -rq - rs
    else rs <- -rq - rr

    struct (rq, rr, rs)

  /// Iterates the ring cells at `radius` via callback. Zero allocation.
  let inline internal forEachRing
    col
    row
    radius
    w
    h
    (orientation: HexOrientation)
    ([<InlineIfLambda>] action: int -> int -> unit)
    : unit =
    if radius > 0 then
      let struct (cq, cr, _) = offsetToCube col row orientation

      // Start at direction 4 corner: center + (0, radius, -radius)
      let mutable q = cq
      let mutable r = cr + radius

      // Walk each of 6 sides
      for side in 0..5 do
        for _ in 1..radius do
          let struct (oc, oR) = cubeToOffset q r orientation

          if oc >= 0 && oc < w && oR >= 0 && oR < h then
            action oc oR

          // Step to next neighbor along this side
          // Directions: 0:(1,-1,0) 1:(0,-1,1) 2:(-1,0,1) 3:(-1,1,0) 4:(0,1,-1) 5:(1,0,-1)
          match side with
          | 0 ->
            q <- q + 1
            r <- r - 1
          | 1 -> r <- r - 1
          | 2 -> q <- q - 1
          | 3 ->
            q <- q - 1
            r <- r + 1
          | 4 -> r <- r + 1
          | 5 -> q <- q + 1
          | _ -> ()

  /// Iterates the cells within `range` hex steps via callback. Zero allocation.
  let inline internal forEachInRange
    col
    row
    range
    w
    h
    (orientation: HexOrientation)
    ([<InlineIfLambda>] action: int -> int -> unit)
    : unit =
    if range >= 0 then
      let struct (cq, cr, _) = offsetToCube col row orientation

      for dq in -range .. range do
        let r1 = max -range (-dq - range)
        let r2 = min range (-dq + range)

        for dr in r1..r2 do
          let q = cq + dq
          let r = cr + dr
          let struct (oc, oR) = cubeToOffset q r orientation

          if oc >= 0 && oc < w && oR >= 0 && oR < h then
            action oc oR

  /// Returns the 6 hex neighbors of (col, row), filtered to grid bounds.
  let inline neighbors col row (grid: HexGrid<'T>) : struct (int * int)[] =
    let struct (w, h) = struct (grid.Width, grid.Height)
    let mutable n = 0

    Internal.forEachNeighbor col row w h grid.Orientation (fun _ _ ->
      n <- n + 1)

    let result = Array.zeroCreate<struct (int * int)> n
    let mutable i = 0

    Internal.forEachNeighbor col row w h grid.Orientation (fun nc nr ->
      result.[i] <- struct (nc, nr)
      i <- i + 1)

    result

  /// Hex distance using cube coordinates.
  let inline distance c1 r1 c2 r2 (grid: HexGrid<'T>) : int =
    let struct (q1, r1c, s1) = offsetToCube c1 r1 grid.Orientation
    let struct (q2, r2c, s2) = offsetToCube c2 r2 grid.Orientation
    (abs(q1 - q2) + abs(r1c - r2c) + abs(s1 - s2)) / 2

  /// Converts a world position to the nearest hex cell coordinates.
  /// Returns ValueNone if outside the grid.
  let inline worldToCell
    (worldPos: Vector2)
    (grid: HexGrid<'T>)
    : struct (int * int) voption =
    let struct (hexW, hexH) =
      match grid.Orientation with
      | PointyTop -> struct (grid.Size * sqrt 3f, grid.Size * 2f)
      | FlatTop -> struct (grid.Size * 2f, grid.Size * sqrt 3f)

    let px = worldPos.X - grid.Origin.X
    let py = worldPos.Y - grid.Origin.Y

    match grid.Orientation with
    | PointyTop ->
      let ax = px - hexW / 2f
      let ay = py - hexH / 2f
      let q = (sqrt 3f / 3f * ax - 1f / 3f * ay) / grid.Size
      let r = (2f / 3f * ay) / grid.Size
      let s = -q - r
      let struct (rq, rr, rs) = cubeRound q r s
      let struct (col, row) = cubeToOffset rq rr grid.Orientation

      if col >= 0 && col < grid.Width && row >= 0 && row < grid.Height then
        ValueSome(struct (col, row))
      else
        ValueNone
    | FlatTop ->
      let ax = px - hexW / 2f
      let ay = py - hexH / 2f
      let q = (2f / 3f * ax) / grid.Size
      let r = (-1f / 3f * ax + sqrt 3f / 3f * ay) / grid.Size
      let s = -q - r
      let struct (rq, rr, rs) = cubeRound q r s
      let struct (col, row) = cubeToOffset rq rr grid.Orientation

      if col >= 0 && col < grid.Width && row >= 0 && row < grid.Height then
        ValueSome(struct (col, row))
      else
        ValueNone

  /// Returns all hex cells within `range` hex steps of (col, row).
  let inline inRange col row range (grid: HexGrid<'T>) : struct (int * int)[] =
    if range < 0 then
      Array.empty
    else
      let struct (w, h) = struct (grid.Width, grid.Height)
      let mutable n = 0
      forEachInRange col row range w h grid.Orientation (fun _ _ -> n <- n + 1)
      let result = Array.zeroCreate<struct (int * int)> n
      let mutable i = 0

      forEachInRange col row range w h grid.Orientation (fun oc oR ->
        result.[i] <- struct (oc, oR)
        i <- i + 1)

      result

  /// Returns all hex cells exactly `radius` hex steps from (col, row).
  let inline ring col row radius (grid: HexGrid<'T>) : struct (int * int)[] =
    if radius < 0 then
      Array.empty
    elif radius = 0 then
      let struct (w, h) = struct (grid.Width, grid.Height)

      if col >= 0 && col < w && row >= 0 && row < h then
        [| struct (col, row) |]
      else
        Array.empty
    else
      let struct (w, h) = struct (grid.Width, grid.Height)
      let scratch = ArrayPool.Shared.Rent(6 * radius)
      let mutable count = 0

      forEachRing col row radius w h grid.Orientation (fun oc oR ->
        scratch.[count] <- struct (oc, oR)
        count <- count + 1)

      let result = Array.zeroCreate<struct (int * int)> count
      Array.blit scratch 0 result 0 count
      ArrayPool.Shared.Return(scratch)
      result

  /// Returns all hex cells within `radius` hex steps, in spiral order
  /// (center first, then ring 1, ring 2, ...).
  let inline spiral col row radius (grid: HexGrid<'T>) : struct (int * int)[] =
    if radius < 0 then
      Array.empty
    else
      let struct (w, h) = struct (grid.Width, grid.Height)
      let scratch = ArrayPool.Shared.Rent(1 + 3 * radius * (radius + 1))
      let mutable count = 0

      if col >= 0 && col < w && row >= 0 && row < h then
        scratch.[0] <- struct (col, row)
        count <- 1

      for r in 1..radius do
        forEachRing col row r w h grid.Orientation (fun oc oR ->
          scratch.[count] <- struct (oc, oR)
          count <- count + 1)

      let result = Array.zeroCreate<struct (int * int)> count
      Array.blit scratch 0 result 0 count
      ArrayPool.Shared.Return(scratch)
      result

  /// Returns true if a hex line from (c1,r1) to (c2,r2) is clear of blocked cells.
  /// The start cell is not checked; the goal IS checked.
  let inline lineOfSight
    c1
    r1
    c2
    r2
    ([<InlineIfLambda>] isBlocked: int -> int -> bool)
    (grid: HexGrid<'T>)
    : bool =
    let struct (w, h) = struct (grid.Width, grid.Height)
    let struct (q1, r1c, s1) = offsetToCube c1 r1 grid.Orientation
    let struct (q2, r2c, s2) = offsetToCube c2 r2 grid.Orientation
    let n = max (abs(q2 - q1)) (max (abs(r2c - r1c)) (abs(s2 - s1)))

    if n = 0 then
      true
    else
      let mutable blocked = false
      let mutable i = 1

      while i <= n && not blocked do
        let t = float32 i / float32 n
        let fq = float32 q1 + (float32(q2 - q1)) * t
        let fr = float32 r1c + (float32(r2c - r1c)) * t
        let fs = float32 s1 + (float32(s2 - s1)) * t
        let struct (cq, cr, cs) = cubeRound fq fr fs
        let struct (col, row) = cubeToOffset cq cr grid.Orientation

        if col >= 0 && col < w && row >= 0 && row < h then
          if isBlocked col row then
            blocked <- true
        else
          blocked <- true

        i <- i + 1

      not blocked

  /// Returns the visible hex cells along a line from (c1,r1) toward (c2,r2),
  /// stopping at the first blocked cell.
  let inline lineOfSightCells
    c1
    r1
    c2
    r2
    ([<InlineIfLambda>] isBlocked: int -> int -> bool)
    (grid: HexGrid<'T>)
    : struct (int * int)[] =
    let struct (w, h) = struct (grid.Width, grid.Height)
    let struct (q1, r1c, s1) = offsetToCube c1 r1 grid.Orientation
    let struct (q2, r2c, s2) = offsetToCube c2 r2 grid.Orientation
    let n = max (abs(q2 - q1)) (max (abs(r2c - r1c)) (abs(s2 - s1)))
    let scratch = ArrayPool.Shared.Rent(n + 1)
    let mutable count = 0

    if c1 >= 0 && c1 < w && r1 >= 0 && r1 < h && not(isBlocked c1 r1) then
      scratch.[count] <- struct (c1, r1)
      count <- count + 1

      let mutable stopped = false
      let mutable i = 1

      while i <= n && not stopped do
        let t = float32 i / float32 n
        let fq = float32 q1 + (float32(q2 - q1)) * t
        let fr = float32 r1c + (float32(r2c - r1c)) * t
        let fs = float32 s1 + (float32(s2 - s1)) * t
        let struct (cq, cr, cs) = cubeRound fq fr fs
        let struct (col, row) = cubeToOffset cq cr grid.Orientation

        if col >= 0 && col < w && row >= 0 && row < h then
          if isBlocked col row then
            stopped <- true
          else
            scratch.[count] <- struct (col, row)
            count <- count + 1
        else
          stopped <- true

        i <- i + 1

    let result = Array.zeroCreate<struct (int * int)> count
    Array.blit scratch 0 result 0 count
    ArrayPool.Shared.Return(scratch)
    result

  /// Flood fill from (col, row) using BFS over hex neighbors.
  /// Returns all reachable hex cells for which `predicate` returns true.
  let inline floodFill
    col
    row
    ([<InlineIfLambda>] predicate: int -> int -> bool)
    (grid: HexGrid<'T>)
    : struct (int * int)[] =
    let struct (w, h) = struct (grid.Width, grid.Height)

    if w = 0 || h = 0 then
      Array.empty
    elif col < 0 || col >= w || row < 0 || row >= h then
      Array.empty
    elif not(predicate col row) then
      Array.empty
    else
      let total = w * h
      let visited = ArrayPool.Shared.Rent(total)
      Array.Clear(visited, 0, total)
      let queue = ArrayPool.Shared.Rent(total)
      let mutable head = 0
      let mutable tail = 0
      queue.[tail] <- struct (col, row)
      tail <- tail + 1
      visited.[col + row * w] <- 1

      while head < tail do
        let struct (cc, cr) = queue.[head]
        head <- head + 1

        Internal.forEachNeighbor cc cr w h grid.Orientation (fun nc nr ->
          let idx = nc + nr * w

          if visited.[idx] = 0 && predicate nc nr then
            visited.[idx] <- 1
            queue.[tail] <- struct (nc, nr)
            tail <- tail + 1)

      let result = Array.zeroCreate<struct (int * int)> tail
      Array.blit queue 0 result 0 tail
      ArrayPool.Shared.Return(queue)
      ArrayPool.Shared.Return(visited)
      result

  /// A* pathfinding on a hex grid. Returns the shortest path from start to
  /// goal as an array of hex coordinates, or ValueNone if no path exists.
  let inline findPath
    startCol
    startRow
    goalCol
    goalRow
    ([<InlineIfLambda>] isPassable: int -> int -> bool)
    ([<InlineIfLambda>] costFn: int -> int -> int -> int -> float32)
    (grid: HexGrid<'T>)
    : struct (int * int)[] voption =
    let struct (w, h) = struct (grid.Width, grid.Height)

    if
      startCol < 0
      || startCol >= w
      || startRow < 0
      || startRow >= h
      || goalCol < 0
      || goalCol >= w
      || goalRow < 0
      || goalRow >= h
    then
      ValueNone
    elif
      not(isPassable startCol startRow) || not(isPassable goalCol goalRow)
    then
      ValueNone
    elif startCol = goalCol && startRow = goalRow then
      ValueSome [| struct (startCol, startRow) |]
    else
      let struct (gq, gr, _) = offsetToCube goalCol goalRow grid.Orientation

      let inline hCost
        (gq: int)
        (gr: int)
        (orientation: HexOrientation)
        c
        r
        : float32 =
        let struct (q, rc, _) = offsetToCube c r orientation
        let dq = gq - q
        let dr = gr - rc
        float32(abs dq + abs dr + abs(dq + dr)) / 2f

      let total = w * h
      let gScore = ArrayPool.Shared.Rent(total)
      let parentCol = ArrayPool.Shared.Rent(total)
      let parentRow = ArrayPool.Shared.Rent(total)
      let closed = ArrayPool.Shared.Rent(total)

      for i in 0 .. total - 1 do
        gScore.[i] <- infinityf
        parentCol.[i] <- -1
        parentRow.[i] <- -1
        closed.[i] <- 0

      gScore.[startCol + startRow * w] <- 0f
      let mutable heap = Internal.create total

      Internal.push
        &heap
        ({
          Internal.Col = startCol
          Internal.Row = startRow
          Internal.Priority = hCost gq gr grid.Orientation startCol startRow
        })

      let mutable found = false

      while Internal.count heap > 0 && not found do
        match Internal.tryPop &heap with
        | ValueNone -> ()
        | ValueSome current ->
          let struct (cc, cr) = struct (current.Col, current.Row)
          let idx = cc + cr * w

          if closed.[idx] <> 0 then
            ()
          elif cc = goalCol && cr = goalRow then
            found <- true
          else
            closed.[idx] <- 1

            Internal.forEachNeighbor cc cr w h grid.Orientation (fun nc nr ->
              let nIdx = nc + nr * w

              if closed.[nIdx] = 0 && isPassable nc nr then
                let tentative = gScore.[idx] + costFn cc cr nc nr

                if tentative < gScore.[nIdx] then
                  gScore.[nIdx] <- tentative
                  parentCol.[nIdx] <- cc
                  parentRow.[nIdx] <- cr

                  Internal.push
                    &heap
                    ({
                      Internal.Col = nc
                      Internal.Row = nr
                      Internal.Priority =
                        tentative + hCost gq gr grid.Orientation nc nr
                    }))

      let result =
        if found then
          let mutable n = 0
          let mutable cc = goalCol
          let mutable cr = goalRow

          while cc <> startCol || cr <> startRow do
            n <- n + 1
            let idx = cc + cr * w
            cc <- parentCol.[idx]
            cr <- parentRow.[idx]

          n <- n + 1
          let path = Array.zeroCreate<struct (int * int)> n
          let mutable i = n - 1
          cc <- goalCol
          cr <- goalRow

          while cc <> startCol || cr <> startRow do
            path.[i] <- struct (cc, cr)
            i <- i - 1
            let idx = cc + cr * w
            cc <- parentCol.[idx]
            cr <- parentRow.[idx]

          path.[0] <- struct (startCol, startRow)
          ValueSome path
        else
          ValueNone

      ArrayPool.Shared.Return(gScore)
      ArrayPool.Shared.Return(parentCol)
      ArrayPool.Shared.Return(parentRow)
      ArrayPool.Shared.Return(closed)
      Internal.dispose &heap
      result
