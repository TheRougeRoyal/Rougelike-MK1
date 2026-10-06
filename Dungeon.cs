using Microsoft.Xna.Framework;

namespace Roguelike;

/// <summary>
/// Tile types available in the dungeon.
/// </summary>
public enum TileType
{
    /// <summary>A solid tile that blocks movement and sight.</summary>
    Wall,

    /// <summary>A walkable tile.</summary>
    Floor,

    /// <summary>A walkable tile that leads to the next level.</summary>
    Stairs
}

/// <summary>
/// A procedurally generated dungeon level and its visibility state.
/// </summary>
public sealed class Dungeon
{
    private const int MinimumRooms = 2;
    private const int MaximumGenerationAttempts = 5000;
    private const int FovRadius = 8;

    private readonly TileType[,] tiles;
    private readonly bool[,] visible;
    private readonly bool[,] explored;
    private readonly List<Rectangle> rooms = new();

    /// <summary>
    /// Gets the dungeon width in tiles.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the dungeon height in tiles.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the tile at a position.
    /// </summary>
    /// <param name="position">The tile position.</param>
    public TileType this[Point position] => tiles[position.X, position.Y];

    /// <summary>
    /// Initializes and generates a dungeon using the supplied random source.
    /// </summary>
    /// <param name="width">The dungeon width in tiles.</param>
    /// <param name="height">The dungeon height in tiles.</param>
    /// <param name="random">The seeded random source.</param>
    public Dungeon(int width, int height, IRandom random)
    {
        Width = width;
        Height = height;
        tiles = new TileType[width, height];
        visible = new bool[width, height];
        explored = new bool[width, height];
        Generate(random);
    }

    /// <summary>Compatibility constructor for older callers.</summary>
    public Dungeon(int width, int height, Random random) : this(width, height, new RandomAdapter(random)) { }

    /// <summary>Creates a dungeon from a hand-built tile matrix.</summary>
    /// <param name="map">Tiles indexed by x then y.</param>
    /// <param name="playerStart">Player start position.</param>
    /// <param name="stairsPosition">Stairs position.</param>
    public Dungeon(TileType[,] map, Point playerStart, Point stairsPosition)
    {
        ArgumentNullException.ThrowIfNull(map);
        Width = map.GetLength(0);
        Height = map.GetLength(1);
        if (Width == 0 || Height == 0) throw new ArgumentException("Map must not be empty.", nameof(map));
        tiles = (TileType[,])map.Clone();
        visible = new bool[Width, Height];
        explored = new bool[Width, Height];
        PlayerStart = playerStart;
        StairsPosition = stairsPosition;
        StartRoom = new Rectangle(playerStart.X, playerStart.Y, 1, 1);
        LayoutFingerprint = ComputeLayoutFingerprint();
    }

    /// <summary>
    /// Gets the generated player start position.
    /// </summary>
    public Point PlayerStart { get; private set; }

    /// <summary>
    /// Gets the room containing the player start position.
    /// </summary>
    public Rectangle StartRoom { get; private set; }

    /// <summary>
    /// Gets the generated stairs position.
    /// </summary>
    public Point StairsPosition { get; private set; }

    /// <summary>
    /// Gets a deterministic fingerprint of the generated tile layout.
    /// </summary>
    public ulong LayoutFingerprint { get; private set; }

    /// <summary>
    /// Returns whether a tile has been explored.
    /// </summary>
    /// <param name="position">The tile position.</param>
    public bool IsExplored(Point position) => InBounds(position) && explored[position.X, position.Y];

    /// <summary>
    /// Returns whether a tile is currently visible.
    /// </summary>
    /// <param name="position">The tile position.</param>
    public bool IsVisible(Point position) => InBounds(position) && visible[position.X, position.Y];

    /// <summary>Reveals all walkable tiles and their adjacent walls without making them visible.</summary>
    public void RevealAll()
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            Point point = new(x, y);
            if (!IsWalkable(point)) continue;
            explored[x, y] = true;
            foreach (Point neighbor in Neighbors(point))
                if (InBounds(neighbor)) explored[neighbor.X, neighbor.Y] = true;
        }
    }

    /// <summary>
    /// Returns whether a position is inside the map.
    /// </summary>
    /// <param name="position">The tile position.</param>
    public bool InBounds(Point position) =>
        position.X >= 0 && position.X < Width && position.Y >= 0 && position.Y < Height;

    /// <summary>
    /// Returns whether an actor can walk onto a tile.
    /// </summary>
    /// <param name="position">The tile position.</param>
    public bool IsWalkable(Point position) =>
        InBounds(position) && tiles[position.X, position.Y] != TileType.Wall;

    /// <summary>Returns whether a straight line between two tiles is unobstructed.</summary>
    public bool HasLineOfSight(Point origin, Point target)
    {
        if (!InBounds(origin) || !InBounds(target)) return false;
        int x = origin.X;
        int y = origin.Y;
        int deltaX = Math.Abs(target.X - origin.X);
        int deltaY = Math.Abs(target.Y - origin.Y);
        int stepX = x < target.X ? 1 : -1;
        int stepY = y < target.Y ? 1 : -1;
        int error = deltaX - deltaY;
        while (true)
        {
            if (new Point(x, y) != origin && tiles[x, y] == TileType.Wall)
                return new Point(x, y) == target;
            if (x == target.X && y == target.Y) return true;
            int doubledError = error * 2;
            if (doubledError > -deltaY) { error -= deltaY; x += stepX; }
            if (doubledError < deltaX) { error += deltaX; y += stepY; }
        }
    }

    /// <summary>
    /// Recomputes radius-limited line-of-sight around a player.
    /// </summary>
    /// <param name="origin">The player's current tile position.</param>
    public void UpdateFieldOfView(Point origin)
    {
        Array.Clear(visible);

        int minX = Math.Max(0, origin.X - FovRadius);
        int maxX = Math.Min(Width - 1, origin.X + FovRadius);
        int minY = Math.Max(0, origin.Y - FovRadius);
        int maxY = Math.Min(Height - 1, origin.Y + FovRadius);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int deltaX = x - origin.X;
                int deltaY = y - origin.Y;
                if (deltaX * deltaX + deltaY * deltaY > FovRadius * FovRadius)
                {
                    continue;
                }

                Point target = new(x, y);
                if (HasLineOfSight(origin, target))
                {
                    visible[x, y] = true;
                    explored[x, y] = true;
                }
            }
        }
    }

    private void Generate(IRandom random)
    {
        for (int attempt = 0; attempt < MaximumGenerationAttempts; attempt++)
        {
            FillWithWalls();
            rooms.Clear();

            int roomAttempts = random.Next(45, 80);
            for (int roomAttempt = 0; roomAttempt < roomAttempts; roomAttempt++)
            {
                int roomWidth = random.Next(4, 10);
                int roomHeight = random.Next(4, 7);
                int x = random.Next(1, Width - roomWidth - 1);
                int y = random.Next(1, Height - roomHeight - 1);
                Rectangle candidate = new(x, y, roomWidth, roomHeight);

                if (rooms.Any(room => Expanded(room).Intersects(candidate)))
                {
                    continue;
                }

                CarveRoom(candidate);
                rooms.Add(candidate);
            }

            if (rooms.Count < MinimumRooms)
            {
                continue;
            }

            for (int index = 1; index < rooms.Count; index++)
            {
                ConnectRooms(rooms[index - 1].Center, rooms[index].Center, random.Next(2) == 0);
            }

            PlayerStart = rooms[0].Center;
            StartRoom = rooms[0];
            StairsPosition = rooms[^1].Center;
            tiles[StairsPosition.X, StairsPosition.Y] = TileType.Stairs;

            if (IsConnected(PlayerStart, StairsPosition))
            {
                Array.Clear(visible);
                Array.Clear(explored);
                LayoutFingerprint = ComputeLayoutFingerprint();
                return;
            }
        }

        throw new InvalidOperationException("Dungeon generation failed after the maximum number of attempts.");
    }

    private void CarveRoom(Rectangle room)
    {
        for (int y = room.Top; y < room.Bottom; y++)
        {
            for (int x = room.Left; x < room.Right; x++)
            {
                tiles[x, y] = TileType.Floor;
            }
        }
    }

    private void FillWithWalls()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                tiles[x, y] = TileType.Wall;
            }
        }
    }

    private void ConnectRooms(Point start, Point end, bool horizontalFirst)
    {
        if (horizontalFirst)
        {
            CarveHorizontal(start.X, end.X, start.Y);
            CarveVertical(start.Y, end.Y, end.X);
        }
        else
        {
            CarveVertical(start.Y, end.Y, start.X);
            CarveHorizontal(start.X, end.X, end.Y);
        }
    }

    private void CarveHorizontal(int startX, int endX, int y)
    {
        int direction = Math.Sign(endX - startX);
        for (int x = startX; ; x += direction)
        {
            CarveFloor(new Point(x, y));
            if (x == endX)
            {
                break;
            }
        }
    }

    private void CarveVertical(int startY, int endY, int x)
    {
        int direction = Math.Sign(endY - startY);
        for (int y = startY; ; y += direction)
        {
            CarveFloor(new Point(x, y));
            if (y == endY)
            {
                break;
            }
        }
    }

    private void CarveFloor(Point position)
    {
        if (InBounds(position))
        {
            tiles[position.X, position.Y] = TileType.Floor;
        }
    }

    private bool IsConnected(Point start, Point goal)
    {
        bool[,] visited = new bool[Width, Height];
        Queue<Point> pending = new();
        pending.Enqueue(start);
        visited[start.X, start.Y] = true;

        while (pending.Count > 0)
        {
            Point current = pending.Dequeue();
            if (current == goal)
            {
                return true;
            }

            foreach (Point neighbor in Neighbors(current))
            {
                if (IsWalkable(neighbor) && !visited[neighbor.X, neighbor.Y])
                {
                    visited[neighbor.X, neighbor.Y] = true;
                    pending.Enqueue(neighbor);
                }
            }
        }

        return false;
    }

    private ulong ComputeLayoutFingerprint()
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong fingerprint = offset;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                fingerprint ^= (byte)tiles[x, y];
                fingerprint *= prime;
            }
        }

        return fingerprint;
    }

    private IEnumerable<Point> Neighbors(Point position)
    {
        yield return new Point(position.X - 1, position.Y);
        yield return new Point(position.X + 1, position.Y);
        yield return new Point(position.X, position.Y - 1);
        yield return new Point(position.X, position.Y + 1);
    }

    private static Rectangle Expanded(Rectangle room) =>
        new(room.X - 1, room.Y - 1, room.Width + 2, room.Height + 2);
}
