namespace Mibo.Layout3D

open System
open System.Buffers
open System.Numerics
open Mibo.Layout

// ── Voxel grid spatial helpers ──────────────────────────────────────────

module Grid3DSpatial =

  let inline internal toIndex x y z w h = x + y * w + z * w * h

  /// Internal helpers for A* pathfinding. Not intended for direct use.
  module Internal =

    [<Struct>]
    type AStarNode = {
      X: int
      Y: int
      Z: int
      Priority: float32
    }

    /// Min-heap priority queue for 3D A* pathfinding over a pooled backing array.
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

  /// Returns the 6 face-adjacent neighbors of (x, y, z).
  let inline neighbors6
    x
    y
    z
    (grid: CellGrid3D<'T>)
    : struct (int * int * int)[] =
    let struct (w, h, d) = struct (grid.Width, grid.Height, grid.Depth)
    let mutable n = 0

    if x > 0 then
      n <- n + 1

    if x < w - 1 then
      n <- n + 1

    if y > 0 then
      n <- n + 1

    if y < h - 1 then
      n <- n + 1

    if z > 0 then
      n <- n + 1

    if z < d - 1 then
      n <- n + 1

    let result = Array.zeroCreate<struct (int * int * int)> n
    let mutable i = 0

    if x > 0 then
      result.[i] <- struct (x - 1, y, z)
      i <- i + 1

    if x < w - 1 then
      result.[i] <- struct (x + 1, y, z)
      i <- i + 1

    if y > 0 then
      result.[i] <- struct (x, y - 1, z)
      i <- i + 1

    if y < h - 1 then
      result.[i] <- struct (x, y + 1, z)
      i <- i + 1

    if z > 0 then
      result.[i] <- struct (x, y, z - 1)
      i <- i + 1

    if z < d - 1 then
      result.[i] <- struct (x, y, z + 1)
      i <- i + 1

    result

  /// Returns the 26 surrounding neighbors (face + edge + corner).
  let inline neighbors26
    x
    y
    z
    (grid: CellGrid3D<'T>)
    : struct (int * int * int)[] =
    let struct (w, h, d) = struct (grid.Width, grid.Height, grid.Depth)
    let mutable n = 0

    for dz in -1 .. 1 do
      for dy in -1 .. 1 do
        for dx in -1 .. 1 do
          if dx <> 0 || dy <> 0 || dz <> 0 then
            let struct (nx, ny, nz) = struct (x + dx, y + dy, z + dz)

            if nx >= 0 && nx < w && ny >= 0 && ny < h && nz >= 0 && nz < d then
              n <- n + 1

    let result = Array.zeroCreate<struct (int * int * int)> n
    let mutable i = 0

    for dz in -1 .. 1 do
      for dy in -1 .. 1 do
        for dx in -1 .. 1 do
          if dx <> 0 || dy <> 0 || dz <> 0 then
            let struct (nx, ny, nz) = struct (x + dx, y + dy, z + dz)

            if nx >= 0 && nx < w && ny >= 0 && ny < h && nz >= 0 && nz < d then
              result.[i] <- struct (nx, ny, nz)
              i <- i + 1

    result

  /// Manhattan distance in 3D.
  let inline distanceManhattan x1 y1 z1 x2 y2 z2 : int =
    abs(x2 - x1) + abs(y2 - y1) + abs(z2 - z1)

  /// Chebyshev distance in 3D (diagonal = 1).
  let inline distanceChebyshev x1 y1 z1 x2 y2 z2 : int =
    max (abs(x2 - x1)) (max (abs(y2 - y1)) (abs(z2 - z1)))

  /// Euclidean distance in 3D.
  let inline distanceEuclidean x1 y1 z1 x2 y2 z2 : float32 =
    let dx = float32(x2 - x1)
    let dy = float32(y2 - y1)
    let dz = float32(z2 - z1)
    sqrt(dx * dx + dy * dy + dz * dz)

  /// Converts a world position to the nearest grid cell. Returns ValueNone
  /// if the position is outside the grid.
  let inline worldToCell
    (worldPos: Vector3)
    (grid: CellGrid3D<'T>)
    : struct (int * int * int) voption =
    let fx = (worldPos.X - grid.Origin.X) / grid.CellSize.X
    let fy = (worldPos.Y - grid.Origin.Y) / grid.CellSize.Y
    let fz = (worldPos.Z - grid.Origin.Z) / grid.CellSize.Z
    let cx = int(round fx)
    let cy = int(round fy)
    let cz = int(round fz)

    if
      cx >= 0
      && cx < grid.Width
      && cy >= 0
      && cy < grid.Height
      && cz >= 0
      && cz < grid.Depth
    then
      ValueSome(struct (cx, cy, cz))
    else
      ValueNone

  /// Returns all cells within Chebyshev distance `range` of (x, y, z).
  let inline inRange
    x
    y
    z
    range
    (grid: CellGrid3D<'T>)
    : struct (int * int * int)[] =
    if range < 0 then
      Array.empty
    else
      let struct (w, h, d) = struct (grid.Width, grid.Height, grid.Depth)
      let x1 = max 0 (x - range)
      let x2 = min (w - 1) (x + range)
      let y1 = max 0 (y - range)
      let y2 = min (h - 1) (y + range)
      let z1 = max 0 (z - range)
      let z2 = min (d - 1) (z + range)
      let mutable n = 0

      for cz in z1..z2 do
        for cy in y1..y2 do
          for cx in x1..x2 do
            if max (abs(cx - x)) (max (abs(cy - y)) (abs(cz - z))) <= range then
              n <- n + 1

      let result = Array.zeroCreate<struct (int * int * int)> n
      let mutable i = 0

      for cz in z1..z2 do
        for cy in y1..y2 do
          for cx in x1..x2 do
            if max (abs(cx - x)) (max (abs(cy - y)) (abs(cz - z))) <= range then
              result.[i] <- struct (cx, cy, cz)
              i <- i + 1

      result

  /// Returns true if a 3D line from (x1,y1,z1) to (x2,y2,z2) is clear of
  /// blocked cells. Uses 3D Bresenham. Start not checked; goal IS checked.
  let inline lineOfSight
    x1
    y1
    z1
    x2
    y2
    z2
    ([<InlineIfLambda>] isBlocked: int -> int -> int -> bool)
    (grid: CellGrid3D<'T>)
    : bool =
    let struct (w, h, d) = struct (grid.Width, grid.Height, grid.Depth)
    let dx = abs(x2 - x1)
    let dy = abs(y2 - y1)
    let dz = abs(z2 - z1)
    let sx = if x1 < x2 then 1 else -1
    let sy = if y1 < y2 then 1 else -1
    let sz = if z1 < z2 then 1 else -1
    let dm = max dx (max dy dz)
    let mutable struct (cx, cy, cz) = struct (x1, y1, z1)
    let mutable ex = dm / 2
    let mutable ey = dm / 2
    let mutable ez = dm / 2
    let mutable blocked = false

    for _ in 1..dm do
      if not blocked then
        ex <- ex - dx

        if ex < 0 then
          ex <- ex + dm
          cx <- cx + sx

        ey <- ey - dy

        if ey < 0 then
          ey <- ey + dm
          cy <- cy + sy

        ez <- ez - dz

        if ez < 0 then
          ez <- ez + dm
          cz <- cz + sz

        if cx >= 0 && cx < w && cy >= 0 && cy < h && cz >= 0 && cz < d then
          if isBlocked cx cy cz then
            blocked <- true
        else
          blocked <- true

    not blocked

  /// Returns the visible cells along a 3D line from (x1,y1,z1) toward
  /// (x2,y2,z2), stopping at the first blocked cell.
  let inline lineOfSightCells
    x1
    y1
    z1
    x2
    y2
    z2
    ([<InlineIfLambda>] isBlocked: int -> int -> int -> bool)
    (grid: CellGrid3D<'T>)
    : struct (int * int * int)[] =
    let struct (w, h, d) = struct (grid.Width, grid.Height, grid.Depth)
    let dm = max (abs(x2 - x1)) (max (abs(y2 - y1)) (abs(z2 - z1)))
    let scratch = ArrayPool.Shared.Rent(dm + 1)
    let mutable count = 0

    if
      x1 >= 0
      && x1 < w
      && y1 >= 0
      && y1 < h
      && z1 >= 0
      && z1 < d
      && not(isBlocked x1 y1 z1)
    then
      scratch.[count] <- struct (x1, y1, z1)
      count <- count + 1

      let dx = abs(x2 - x1)
      let dy = abs(y2 - y1)
      let dz = abs(z2 - z1)
      let sx = if x1 < x2 then 1 else -1
      let sy = if y1 < y2 then 1 else -1
      let sz = if z1 < z2 then 1 else -1
      let mutable struct (cx, cy, cz) = struct (x1, y1, z1)
      let mutable ex = dm / 2
      let mutable ey = dm / 2
      let mutable ez = dm / 2
      let mutable stopped = false

      for _ in 1..dm do
        if not stopped then
          ex <- ex - dx

          if ex < 0 then
            ex <- ex + dm
            cx <- cx + sx

          ey <- ey - dy

          if ey < 0 then
            ey <- ey + dm
            cy <- cy + sy

          ez <- ez - dz

          if ez < 0 then
            ez <- ez + dm
            cz <- cz + sz

          if cx >= 0 && cx < w && cy >= 0 && cy < h && cz >= 0 && cz < d then
            if isBlocked cx cy cz then
              stopped <- true
            else
              scratch.[count] <- struct (cx, cy, cz)
              count <- count + 1
          else
            stopped <- true

    let result = Array.zeroCreate<struct (int * int * int)> count
    Array.blit scratch 0 result 0 count
    ArrayPool.Shared.Return(scratch)
    result

  /// Flood fill from (x, y, z) using BFS over 6-connected neighbors.
  let inline floodFill
    x
    y
    z
    ([<InlineIfLambda>] predicate: int -> int -> int -> bool)
    (grid: CellGrid3D<'T>)
    : struct (int * int * int)[] =
    let struct (w, h, d) = struct (grid.Width, grid.Height, grid.Depth)

    if w = 0 || h = 0 || d = 0 then
      Array.empty
    elif x < 0 || x >= w || y < 0 || y >= h || z < 0 || z >= d then
      Array.empty
    elif not(predicate x y z) then
      Array.empty
    else
      let total = w * h * d
      let visited = ArrayPool.Shared.Rent(total)
      Array.Clear(visited, 0, total)
      let queue = ArrayPool.Shared.Rent(total)
      let mutable head = 0
      let mutable tail = 0
      queue.[tail] <- struct (x, y, z)
      tail <- tail + 1
      visited.[toIndex x y z w h] <- 1

      while head < tail do
        let struct (cx, cy, cz) = queue.[head]
        head <- head + 1

        if cx > 0 then
          let nx = cx - 1
          let idx = toIndex nx cy cz w h

          if visited.[idx] = 0 && predicate nx cy cz then
            visited.[idx] <- 1
            queue.[tail] <- struct (nx, cy, cz)
            tail <- tail + 1

        if cx < w - 1 then
          let nx = cx + 1
          let idx = toIndex nx cy cz w h

          if visited.[idx] = 0 && predicate nx cy cz then
            visited.[idx] <- 1
            queue.[tail] <- struct (nx, cy, cz)
            tail <- tail + 1

        if cy > 0 then
          let ny = cy - 1
          let idx = toIndex cx ny cz w h

          if visited.[idx] = 0 && predicate cx ny cz then
            visited.[idx] <- 1
            queue.[tail] <- struct (cx, ny, cz)
            tail <- tail + 1

        if cy < h - 1 then
          let ny = cy + 1
          let idx = toIndex cx ny cz w h

          if visited.[idx] = 0 && predicate cx ny cz then
            visited.[idx] <- 1
            queue.[tail] <- struct (cx, ny, cz)
            tail <- tail + 1

        if cz > 0 then
          let nz = cz - 1
          let idx = toIndex cx cy nz w h

          if visited.[idx] = 0 && predicate cx cy nz then
            visited.[idx] <- 1
            queue.[tail] <- struct (cx, cy, nz)
            tail <- tail + 1

        if cz < d - 1 then
          let nz = cz + 1
          let idx = toIndex cx cy nz w h

          if visited.[idx] = 0 && predicate cx cy nz then
            visited.[idx] <- 1
            queue.[tail] <- struct (cx, cy, nz)
            tail <- tail + 1

      let result = Array.zeroCreate<struct (int * int * int)> tail
      Array.blit queue 0 result 0 tail
      ArrayPool.Shared.Return(queue)
      ArrayPool.Shared.Return(visited)
      result

  /// A* pathfinding on a 3D voxel grid. Returns the shortest path as an
  /// array of coordinates, or ValueNone if no path exists.
  let inline findPath
    startX
    startY
    startZ
    goalX
    goalY
    goalZ
    ([<InlineIfLambda>] isPassable: int -> int -> int -> bool)
    ([<InlineIfLambda>] costFn:
      int -> int -> int -> int -> int -> int -> float32)
    (grid: CellGrid3D<'T>)
    : struct (int * int * int)[] voption =
    let struct (w, h, d) = struct (grid.Width, grid.Height, grid.Depth)

    if
      startX < 0
      || startX >= w
      || startY < 0
      || startY >= h
      || startZ < 0
      || startZ >= d
      || goalX < 0
      || goalX >= w
      || goalY < 0
      || goalY >= h
      || goalZ < 0
      || goalZ >= d
    then
      ValueNone
    elif
      not(isPassable startX startY startZ) || not(isPassable goalX goalY goalZ)
    then
      ValueNone
    elif startX = goalX && startY = goalY && startZ = goalZ then
      ValueSome [| struct (startX, startY, startZ) |]
    else
      let total = w * h * d
      let gScore = ArrayPool.Shared.Rent(total)
      let parentX = ArrayPool.Shared.Rent(total)
      let parentY = ArrayPool.Shared.Rent(total)
      let parentZ = ArrayPool.Shared.Rent(total)
      let closed = ArrayPool.Shared.Rent(total)

      for i in 0 .. total - 1 do
        gScore.[i] <- infinityf
        parentX.[i] <- -1
        parentY.[i] <- -1
        parentZ.[i] <- -1
        closed.[i] <- 0

      let inline hCost (gx: int) (gy: int) (gz: int) x y z : float32 =
        float32(abs(gx - x) + abs(gy - y) + abs(gz - z))

      gScore.[toIndex startX startY startZ w h] <- 0f
      let mutable heap = Internal.create total

      Internal.push
        &heap
        ({
          Internal.X = startX
          Internal.Y = startY
          Internal.Z = startZ
          Internal.Priority = hCost goalX goalY goalZ startX startY startZ
        })

      let mutable found = false

      while Internal.count heap > 0 && not found do
        match Internal.tryPop &heap with
        | ValueNone -> ()
        | ValueSome current ->
          let struct (cx, cy, cz) = struct (current.X, current.Y, current.Z)
          let idx = toIndex cx cy cz w h

          if closed.[idx] <> 0 then
            ()
          elif cx = goalX && cy = goalY && cz = goalZ then
            found <- true
          else
            closed.[idx] <- 1

            let mutable nx = cx - 1

            if
              nx >= 0
              && closed.[toIndex nx cy cz w h] = 0
              && isPassable nx cy cz
            then
              let nIdx = toIndex nx cy cz w h
              let tentative = gScore.[idx] + costFn cx cy cz nx cy cz

              if tentative < gScore.[nIdx] then
                gScore.[nIdx] <- tentative
                parentX.[nIdx] <- cx
                parentY.[nIdx] <- cy
                parentZ.[nIdx] <- cz

                Internal.push
                  &heap
                  ({
                    Internal.X = nx
                    Internal.Y = cy
                    Internal.Z = cz
                    Internal.Priority =
                      tentative + hCost goalX goalY goalZ nx cy cz
                  })

            nx <- cx + 1

            if
              nx < w && closed.[toIndex nx cy cz w h] = 0 && isPassable nx cy cz
            then
              let nIdx = toIndex nx cy cz w h
              let tentative = gScore.[idx] + costFn cx cy cz nx cy cz

              if tentative < gScore.[nIdx] then
                gScore.[nIdx] <- tentative
                parentX.[nIdx] <- cx
                parentY.[nIdx] <- cy
                parentZ.[nIdx] <- cz

                Internal.push
                  &heap
                  ({
                    Internal.X = nx
                    Internal.Y = cy
                    Internal.Z = cz
                    Internal.Priority =
                      tentative + hCost goalX goalY goalZ nx cy cz
                  })

            let mutable ny = cy - 1

            if
              ny >= 0
              && closed.[toIndex cx ny cz w h] = 0
              && isPassable cx ny cz
            then
              let nIdx = toIndex cx ny cz w h
              let tentative = gScore.[idx] + costFn cx cy cz cx ny cz

              if tentative < gScore.[nIdx] then
                gScore.[nIdx] <- tentative
                parentX.[nIdx] <- cx
                parentY.[nIdx] <- cy
                parentZ.[nIdx] <- cz

                Internal.push
                  &heap
                  ({
                    Internal.X = cx
                    Internal.Y = ny
                    Internal.Z = cz
                    Internal.Priority =
                      tentative + hCost goalX goalY goalZ cx ny cz
                  })

            ny <- cy + 1

            if
              ny < h && closed.[toIndex cx ny cz w h] = 0 && isPassable cx ny cz
            then
              let nIdx = toIndex cx ny cz w h
              let tentative = gScore.[idx] + costFn cx cy cz cx ny cz

              if tentative < gScore.[nIdx] then
                gScore.[nIdx] <- tentative
                parentX.[nIdx] <- cx
                parentY.[nIdx] <- cy
                parentZ.[nIdx] <- cz

                Internal.push
                  &heap
                  ({
                    Internal.X = cx
                    Internal.Y = ny
                    Internal.Z = cz
                    Internal.Priority =
                      tentative + hCost goalX goalY goalZ cx ny cz
                  })

            let mutable nz = cz - 1

            if
              nz >= 0
              && closed.[toIndex cx cy nz w h] = 0
              && isPassable cx cy nz
            then
              let nIdx = toIndex cx cy nz w h
              let tentative = gScore.[idx] + costFn cx cy cz cx cy nz

              if tentative < gScore.[nIdx] then
                gScore.[nIdx] <- tentative
                parentX.[nIdx] <- cx
                parentY.[nIdx] <- cy
                parentZ.[nIdx] <- cz

                Internal.push
                  &heap
                  ({
                    Internal.X = cx
                    Internal.Y = cy
                    Internal.Z = nz
                    Internal.Priority =
                      tentative + hCost goalX goalY goalZ cx cy nz
                  })

            nz <- cz + 1

            if
              nz < d && closed.[toIndex cx cy nz w h] = 0 && isPassable cx cy nz
            then
              let nIdx = toIndex cx cy nz w h
              let tentative = gScore.[idx] + costFn cx cy cz cx cy nz

              if tentative < gScore.[nIdx] then
                gScore.[nIdx] <- tentative
                parentX.[nIdx] <- cx
                parentY.[nIdx] <- cy
                parentZ.[nIdx] <- cz

                Internal.push
                  &heap
                  ({
                    Internal.X = cx
                    Internal.Y = cy
                    Internal.Z = nz
                    Internal.Priority =
                      tentative + hCost goalX goalY goalZ cx cy nz
                  })

      let result =
        if found then
          let mutable n = 0
          let mutable cx = goalX
          let mutable cy = goalY
          let mutable cz = goalZ

          while cx <> startX || cy <> startY || cz <> startZ do
            n <- n + 1
            let idx = toIndex cx cy cz w h
            cx <- parentX.[idx]
            cy <- parentY.[idx]
            cz <- parentZ.[idx]

          n <- n + 1
          let path = Array.zeroCreate<struct (int * int * int)> n
          let mutable i = n - 1
          cx <- goalX
          cy <- goalY
          cz <- goalZ

          while cx <> startX || cy <> startY || cz <> startZ do
            path.[i] <- struct (cx, cy, cz)
            i <- i - 1
            let idx = toIndex cx cy cz w h
            cx <- parentX.[idx]
            cy <- parentY.[idx]
            cz <- parentZ.[idx]

          path.[0] <- struct (startX, startY, startZ)
          ValueSome path
        else
          ValueNone

      ArrayPool.Shared.Return(gScore)
      ArrayPool.Shared.Return(parentX)
      ArrayPool.Shared.Return(parentY)
      ArrayPool.Shared.Return(parentZ)
      ArrayPool.Shared.Return(closed)
      Internal.dispose &heap
      result

// ── Hex3D grid spatial helpers ──────────────────────────────────────────

module Hex3DSpatial =

  let inline internal toIndex col row layer w d = col + row * w + layer * w * d

  /// Returns the 8 neighbors of a hex cell in 3D: 6 hex neighbors on the
  /// same layer plus the cells directly above and below.
  let inline neighbors
    col
    row
    layer
    (grid: HexGrid3D<'T>)
    : struct (int * int * int)[] =
    let struct (w, h, d) = struct (grid.Width, grid.Height, grid.Depth)

    if layer < 0 || layer >= h then
      Array.empty
    else
      let mutable n = 0

      Hex2DSpatial.Internal.forEachNeighbor
        col
        row
        w
        d
        grid.Orientation
        (fun _ _ -> n <- n + 1)

      if layer > 0 then
        n <- n + 1

      if layer < h - 1 then
        n <- n + 1

      let result = Array.zeroCreate<struct (int * int * int)> n
      let mutable i = 0

      Hex2DSpatial.Internal.forEachNeighbor
        col
        row
        w
        d
        grid.Orientation
        (fun nc nr ->
          result.[i] <- struct (nc, nr, layer)
          i <- i + 1)

      if layer > 0 then
        result.[i] <- struct (col, row, layer - 1)
        i <- i + 1

      if layer < h - 1 then
        result.[i] <- struct (col, row, layer + 1)
        i <- i + 1

      result

  /// Returns only the 6 hex neighbors on the same layer.
  let inline neighborsHex
    col
    row
    layer
    (grid: HexGrid3D<'T>)
    : struct (int * int * int)[] =
    let struct (w, d) = struct (grid.Width, grid.Depth)
    let mutable n = 0

    Hex2DSpatial.Internal.forEachNeighbor
      col
      row
      w
      d
      grid.Orientation
      (fun _ _ -> n <- n + 1)

    let result = Array.zeroCreate<struct (int * int * int)> n
    let mutable i = 0

    Hex2DSpatial.Internal.forEachNeighbor
      col
      row
      w
      d
      grid.Orientation
      (fun nc nr ->
        result.[i] <- struct (nc, nr, layer)
        i <- i + 1)

    result

  /// Hex distance in 3D: hex distance on the plane + layer difference.
  let inline distance c1 r1 l1 c2 r2 l2 (grid: HexGrid3D<'T>) : int =
    let struct (q1, r1c, s1) = Hex2DSpatial.offsetToCube c1 r1 grid.Orientation
    let struct (q2, r2c, s2) = Hex2DSpatial.offsetToCube c2 r2 grid.Orientation
    (abs(q1 - q2) + abs(r1c - r2c) + abs(s1 - s2)) / 2 + abs(l2 - l1)

  /// Converts a 3D world position to the nearest hex3D cell.
  let inline worldToCell
    (worldPos: Vector3)
    (grid: HexGrid3D<'T>)
    : struct (int * int * int) voption =
    let layerF = (worldPos.Y - grid.Origin.Y) / grid.LayerHeight
    let layer = int(round layerF)

    if layer < 0 || layer >= grid.Height then
      ValueNone
    else
      let struct (hexW, hexH) =
        match grid.Orientation with
        | PointyTop -> struct (grid.HexSize * sqrt 3f, grid.HexSize * 2f)
        | FlatTop -> struct (grid.HexSize * 2f, grid.HexSize * sqrt 3f)

      let px = worldPos.X - grid.Origin.X
      let pz = worldPos.Z - grid.Origin.Z

      match grid.Orientation with
      | PointyTop ->
        let ax = px - hexW / 2f
        let az = pz - hexH / 2f
        let q = (sqrt 3f / 3f * ax - 1f / 3f * az) / grid.HexSize
        let r = (2f / 3f * az) / grid.HexSize
        let s = -q - r
        let struct (rq, rr, rs) = Hex2DSpatial.cubeRound q r s
        let struct (col, row) = Hex2DSpatial.cubeToOffset rq rr grid.Orientation

        if col >= 0 && col < grid.Width && row >= 0 && row < grid.Depth then
          ValueSome(struct (col, row, layer))
        else
          ValueNone
      | FlatTop ->
        let ax = px - hexW / 2f
        let az = pz - hexH / 2f
        let q = (2f / 3f * ax) / grid.HexSize
        let r = (-1f / 3f * ax + sqrt 3f / 3f * az) / grid.HexSize
        let s = -q - r
        let struct (rq, rr, rs) = Hex2DSpatial.cubeRound q r s
        let struct (col, row) = Hex2DSpatial.cubeToOffset rq rr grid.Orientation

        if col >= 0 && col < grid.Width && row >= 0 && row < grid.Depth then
          ValueSome(struct (col, row, layer))
        else
          ValueNone

  /// Returns all hex3D cells within `range` steps (hex + vertical).
  let inline inRange
    col
    row
    layer
    range
    (grid: HexGrid3D<'T>)
    : struct (int * int * int)[] =
    if range < 0 then
      Array.empty
    else
      let struct (w, h, d) = struct (grid.Width, grid.Height, grid.Depth)
      let mutable n = 0

      for dl in -range .. range do
        let l = layer + dl

        if l >= 0 && l < h then
          let hexRange = range - abs dl

          Hex2DSpatial.forEachInRange
            col
            row
            hexRange
            w
            d
            grid.Orientation
            (fun _ _ -> n <- n + 1)

      let result = Array.zeroCreate<struct (int * int * int)> n
      let mutable i = 0

      for dl in -range .. range do
        let l = layer + dl

        if l >= 0 && l < h then
          let hexRange = range - abs dl

          Hex2DSpatial.forEachInRange
            col
            row
            hexRange
            w
            d
            grid.Orientation
            (fun oc oR ->
              result.[i] <- struct (oc, oR, l)
              i <- i + 1)

      result

  /// Returns true if a hex3D line is clear of blocked cells.
  let inline lineOfSight
    c1
    r1
    l1
    c2
    r2
    l2
    ([<InlineIfLambda>] isBlocked: int -> int -> int -> bool)
    (grid: HexGrid3D<'T>)
    : bool =
    let struct (w, h, d) = struct (grid.Width, grid.Height, grid.Depth)
    let struct (q1, r1c, s1) = Hex2DSpatial.offsetToCube c1 r1 grid.Orientation
    let struct (q2, r2c, s2) = Hex2DSpatial.offsetToCube c2 r2 grid.Orientation
    let hexN = max (abs(q2 - q1)) (max (abs(r2c - r1c)) (abs(s2 - s1)))
    let vertN = abs(l2 - l1)
    let n = max hexN vertN

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
        let struct (cq, cr, cs) = Hex2DSpatial.cubeRound fq fr fs
        let struct (col, row) = Hex2DSpatial.cubeToOffset cq cr grid.Orientation
        let layer = l1 + int(round(float32(l2 - l1) * t))

        if
          col >= 0 && col < w && row >= 0 && row < d && layer >= 0 && layer < h
        then
          if isBlocked col row layer then
            blocked <- true
        else
          blocked <- true

        i <- i + 1

      not blocked

  /// Flood fill from (col, row, layer) using BFS over hex3D neighbors.
  let inline floodFill
    col
    row
    layer
    ([<InlineIfLambda>] predicate: int -> int -> int -> bool)
    (grid: HexGrid3D<'T>)
    : struct (int * int * int)[] =
    let struct (w, h, d) = struct (grid.Width, grid.Height, grid.Depth)

    if w = 0 || h = 0 || d = 0 then
      Array.empty
    elif
      col < 0 || col >= w || row < 0 || row >= d || layer < 0 || layer >= h
    then
      Array.empty
    elif not(predicate col row layer) then
      Array.empty
    else
      let total = w * h * d
      let visited = ArrayPool.Shared.Rent(total)
      Array.Clear(visited, 0, total)
      let queue = ArrayPool.Shared.Rent(total)
      let mutable head = 0
      let mutable tail = 0
      queue.[tail] <- struct (col, row, layer)
      tail <- tail + 1
      visited.[toIndex col row layer w d] <- 1

      while head < tail do
        let struct (cc, cr, cl) = queue.[head]
        head <- head + 1

        // Same-layer hex neighbors (zero-alloc callback)
        Hex2DSpatial.Internal.forEachNeighbor
          cc
          cr
          w
          d
          grid.Orientation
          (fun nc nr ->
            let idx = toIndex nc nr cl w d

            if visited.[idx] = 0 && predicate nc nr cl then
              visited.[idx] <- 1
              queue.[tail] <- struct (nc, nr, cl)
              tail <- tail + 1)

        // Up / down neighbors
        if cl > 0 then
          let idx = toIndex cc cr (cl - 1) w d

          if visited.[idx] = 0 && predicate cc cr (cl - 1) then
            visited.[idx] <- 1
            queue.[tail] <- struct (cc, cr, cl - 1)
            tail <- tail + 1

        if cl < h - 1 then
          let idx = toIndex cc cr (cl + 1) w d

          if visited.[idx] = 0 && predicate cc cr (cl + 1) then
            visited.[idx] <- 1
            queue.[tail] <- struct (cc, cr, cl + 1)
            tail <- tail + 1

      let result = Array.zeroCreate<struct (int * int * int)> tail
      Array.blit queue 0 result 0 tail
      ArrayPool.Shared.Return(queue)
      ArrayPool.Shared.Return(visited)
      result

  /// A* pathfinding on a hex3D grid. Returns the shortest path or ValueNone.
  let inline findPath
    startCol
    startRow
    startLayer
    goalCol
    goalRow
    goalLayer
    ([<InlineIfLambda>] isPassable: int -> int -> int -> bool)
    ([<InlineIfLambda>] costFn:
      int -> int -> int -> int -> int -> int -> float32)
    (grid: HexGrid3D<'T>)
    : struct (int * int * int)[] voption =
    let struct (w, h, d) = struct (grid.Width, grid.Height, grid.Depth)

    if
      startCol < 0
      || startCol >= w
      || startRow < 0
      || startRow >= d
      || startLayer < 0
      || startLayer >= h
      || goalCol < 0
      || goalCol >= w
      || goalRow < 0
      || goalRow >= d
      || goalLayer < 0
      || goalLayer >= h
    then
      ValueNone
    elif
      not(isPassable startCol startRow startLayer)
      || not(isPassable goalCol goalRow goalLayer)
    then
      ValueNone
    elif startCol = goalCol && startRow = goalRow && startLayer = goalLayer then
      ValueSome [| struct (startCol, startRow, startLayer) |]
    else
      let struct (gq, gr, _) =
        Hex2DSpatial.offsetToCube goalCol goalRow grid.Orientation

      let inline hCost
        (gq: int)
        (gr: int)
        (gl: int)
        (orientation: HexOrientation)
        c
        r
        l
        : float32 =
        let struct (q, rc, _) = Hex2DSpatial.offsetToCube c r orientation
        let dq = gq - q
        let dr = gr - rc
        let hexDist = float32(abs dq + abs dr + abs(dq + dr)) / 2f
        hexDist + float32(abs(gl - l))

      let total = w * h * d
      let gScore = ArrayPool.Shared.Rent(total)
      let parentCol = ArrayPool.Shared.Rent(total)
      let parentRow = ArrayPool.Shared.Rent(total)
      let parentLayer = ArrayPool.Shared.Rent(total)
      let closed = ArrayPool.Shared.Rent(total)

      for i in 0 .. total - 1 do
        gScore.[i] <- infinityf
        parentCol.[i] <- -1
        parentRow.[i] <- -1
        parentLayer.[i] <- -1
        closed.[i] <- 0

      gScore.[toIndex startCol startRow startLayer w d] <- 0f
      let mutable heap = Grid3DSpatial.Internal.create total

      Grid3DSpatial.Internal.push
        &heap
        ({
          Grid3DSpatial.Internal.X = startCol
          Grid3DSpatial.Internal.Y = startRow
          Grid3DSpatial.Internal.Z = startLayer
          Grid3DSpatial.Internal.Priority =
            hCost gq gr goalLayer grid.Orientation startCol startRow startLayer
        })

      let mutable found = false

      while Grid3DSpatial.Internal.count heap > 0 && not found do
        match Grid3DSpatial.Internal.tryPop &heap with
        | ValueNone -> ()
        | ValueSome current ->
          let struct (cc, cr, cl) = struct (current.X, current.Y, current.Z)
          let idx = toIndex cc cr cl w d

          if closed.[idx] <> 0 then
            ()
          elif cc = goalCol && cr = goalRow && cl = goalLayer then
            found <- true
          else
            closed.[idx] <- 1

            // Same-layer hex neighbors (zero-alloc callback)
            Hex2DSpatial.Internal.forEachNeighbor
              cc
              cr
              w
              d
              grid.Orientation
              (fun nc nr ->
                let nIdx = toIndex nc nr cl w d

                if closed.[nIdx] = 0 && isPassable nc nr cl then
                  let tentative = gScore.[idx] + costFn cc cr cl nc nr cl

                  if tentative < gScore.[nIdx] then
                    gScore.[nIdx] <- tentative
                    parentCol.[nIdx] <- cc
                    parentRow.[nIdx] <- cr
                    parentLayer.[nIdx] <- cl

                    Grid3DSpatial.Internal.push
                      &heap
                      ({
                        Grid3DSpatial.Internal.X = nc
                        Grid3DSpatial.Internal.Y = nr
                        Grid3DSpatial.Internal.Z = cl
                        Grid3DSpatial.Internal.Priority =
                          tentative
                          + hCost gq gr goalLayer grid.Orientation nc nr cl
                      }))

            // Up / down neighbors
            if cl > 0 then
              let nIdx = toIndex cc cr (cl - 1) w d

              if closed.[nIdx] = 0 && isPassable cc cr (cl - 1) then
                let tentative = gScore.[idx] + costFn cc cr cl cc cr (cl - 1)

                if tentative < gScore.[nIdx] then
                  gScore.[nIdx] <- tentative
                  parentCol.[nIdx] <- cc
                  parentRow.[nIdx] <- cr
                  parentLayer.[nIdx] <- cl

                  Grid3DSpatial.Internal.push
                    &heap
                    ({
                      Grid3DSpatial.Internal.X = cc
                      Grid3DSpatial.Internal.Y = cr
                      Grid3DSpatial.Internal.Z = cl - 1
                      Grid3DSpatial.Internal.Priority =
                        tentative
                        + hCost gq gr goalLayer grid.Orientation cc cr (cl - 1)
                    })

            if cl < h - 1 then
              let nIdx = toIndex cc cr (cl + 1) w d

              if closed.[nIdx] = 0 && isPassable cc cr (cl + 1) then
                let tentative = gScore.[idx] + costFn cc cr cl cc cr (cl + 1)

                if tentative < gScore.[nIdx] then
                  gScore.[nIdx] <- tentative
                  parentCol.[nIdx] <- cc
                  parentRow.[nIdx] <- cr
                  parentLayer.[nIdx] <- cl

                  Grid3DSpatial.Internal.push
                    &heap
                    ({
                      Grid3DSpatial.Internal.X = cc
                      Grid3DSpatial.Internal.Y = cr
                      Grid3DSpatial.Internal.Z = cl + 1
                      Grid3DSpatial.Internal.Priority =
                        tentative
                        + hCost gq gr goalLayer grid.Orientation cc cr (cl + 1)
                    })

      let result =
        if found then
          let mutable n = 0
          let mutable cc = goalCol
          let mutable cr = goalRow
          let mutable cl = goalLayer

          while cc <> startCol || cr <> startRow || cl <> startLayer do
            n <- n + 1
            let idx = toIndex cc cr cl w d
            cc <- parentCol.[idx]
            cr <- parentRow.[idx]
            cl <- parentLayer.[idx]

          n <- n + 1
          let path = Array.zeroCreate<struct (int * int * int)> n
          let mutable i = n - 1
          cc <- goalCol
          cr <- goalRow
          cl <- goalLayer

          while cc <> startCol || cr <> startRow || cl <> startLayer do
            path.[i] <- struct (cc, cr, cl)
            i <- i - 1
            let idx = toIndex cc cr cl w d
            cc <- parentCol.[idx]
            cr <- parentRow.[idx]
            cl <- parentLayer.[idx]

          path.[0] <- struct (startCol, startRow, startLayer)
          ValueSome path
        else
          ValueNone

      ArrayPool.Shared.Return(gScore)
      ArrayPool.Shared.Return(parentCol)
      ArrayPool.Shared.Return(parentRow)
      ArrayPool.Shared.Return(parentLayer)
      ArrayPool.Shared.Return(closed)
      Grid3DSpatial.Internal.dispose &heap
      result
