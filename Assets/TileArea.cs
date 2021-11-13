using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public class TileArea : MonoBehaviour
{
    private bool needsMeshCombining;
    private bool needsWallMeshCombining;
    private Mesh mesh;
    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private MeshRenderer meshRenderer;

    private TileGenerator generator;
    private Dictionary<CubeCoord, CellData> cells = new();

    private List<CombineInstance> floorsToAdd = new();
    private Dictionary<CubeCoord, CombineInstance> wallsToCombine = new();
    private Dictionary<CubeCoord, CombineInstance> wallsToCombineVoid = new();

    public Mesh floorMesh;
    public Mesh wallMesh ;
    public Mesh wallMeshVoid ;

    private GameObject pickups;

    public class CellData
    {
        public CubeCoord coord;
        public bool[] walls = new bool[6];
        public Vector3 position;
        public bool hasPickup = Random.value < 0.05f;
    }

    private void Start()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;
        meshCollider = GetComponent<MeshCollider>();
        meshCollider.sharedMesh = mesh;
        meshRenderer = GetComponent<MeshRenderer>();
    }

    void UpdateCombinedMesh()
    {
        if (floorsToAdd.Count > 0)
        {
            floorMesh.CombineMeshes(floorsToAdd.ToArray());
            floorsToAdd.Clear();
            needsMeshCombining = true;
        }
        if (needsWallMeshCombining)
        {
            if (wallsToCombine.Count > 0)
            {
                wallMesh.Clear();
                wallMesh.CombineMeshes(wallsToCombine.Values.ToArray());
                needsMeshCombining = true;
            }
            if (wallsToCombineVoid.Count > 0)
            {
                wallMeshVoid.Clear();
                wallMeshVoid.CombineMeshes(wallsToCombineVoid.Values.ToArray());
                needsMeshCombining = true;
            }
        }
      
        if (needsMeshCombining)
        {
            mesh.CombineMeshes(new []{new CombineInstance(){mesh = floorMesh}, new CombineInstance(){mesh = wallMesh}, new CombineInstance(){mesh = wallMeshVoid}}, false, false);
            needsMeshCombining = false;
        }
    }

    public void InitMeshes(String coord)
    {
        mesh = new();
        mesh.subMeshCount = 3;
        floorMesh = new();
        wallMesh = new();
        wallMeshVoid = new();
        mesh.name = $"Main Mesh {coord}";
        floorMesh.name = $"Floor Mesh {coord}";
        wallMesh.name = $"Wall Mesh {coord}";
        wallMeshVoid.name = $"Wall Mesh Void {coord}";
        mesh.subMeshCount = 3; // FloorTiles / Walls / NavMeshLinkTiles
    }
    
    public void Init(TileGenerator generator, Room room)
    {
        this.generator = generator;
        gameObject.name = "Room " + room.Origin;
        transform.position = room.WorldCenter;
        InitMeshes(room.Origin.ToString());
        InitCells(room.Coords);
        UpdateCombinedMesh();
    }

    public void Init(TileGenerator generator, Hallway hallway)
    {
        this.generator = generator;
        gameObject.name = "Hallway";
        transform.position = Vector3.Lerp(hallway.From.Origin.FlatTopToWorld(generator.floorHeight,  generator.tiledict.TileSize()),
            hallway.To.Origin.FlatTopToWorld(generator.floorHeight,  generator.tiledict.TileSize()), 0.5f);
        InitMeshes($"{hallway.From.Origin}|{hallway.To.Origin}");
        InitCells(hallway.Coords);
        InitLoadTriggers(hallway);
        UpdateCombinedMesh();
    }

    public bool[] GetEdgeWalls(CubeCoord coord)
    {
        var walls = new bool[6];
        var neighbors = coord.FlatTopNeighbors();
        for (var i = 0; i < neighbors.Length; i++)
        {
            var neighbor = neighbors[i];
            walls[i] = !generator.IsCell(neighbor);
        }
        return walls;
    }

    public void UpdateWalls()
    {
        List<CellData> toRespawn = new();
        foreach (var wall in wallsToCombine.ToList())
        {
            var cellData = cells[wall.Key];
            var oldWalls = cellData.walls;
            cellData.walls = GetEdgeWalls(cellData.coord);
            if (!oldWalls.SequenceEqual(cellData.walls))
            {
                toRespawn.Add(cellData);
            }   
        }
        foreach (var cellData in toRespawn)
        {
            SpawnWall(cellData);
        }
        UpdateCombinedMesh();
    }

    private void InitLoadTriggers(Hallway hallway)
    {
        var mainTrigger = new GameObject("TriggerArea");
        mainTrigger.transform.parent = transform;
        mainTrigger.transform.position = transform.position + Vector3.up;
        mainTrigger.AddComponent<LevelLoaderTrigger>().Init(generator, hallway.To, floorMesh);
    }

    private void InitCells(IEnumerable<CubeCoord> coords)
    {
        pickups = new GameObject("Pickups");
        pickups.transform.parent = transform;
        
        foreach (var cellCoord in coords)
        {
            var cellData = new CellData()
            {
                coord = cellCoord,
                position = cellCoord.FlatTopToWorld(generator.floorHeight, generator.tiledict.TileSize()),
                walls = GetEdgeWalls(cellCoord)
            };
            cells[cellCoord] = cellData;
            SpawnFloor(cellData);
            SpawnWall(cellData);
            SpawnPickups(cellData);
        }
    }

    private void SpawnWall(CellData cellData)
    {
        if (TileDictionary.edgeTileMap.TryGetValue(cellData.walls, out var type))
        {
            if (type.type == TileDictionary.EdgeTileType.WALL0)
            {
                wallsToCombine.Remove(cellData.coord);
                wallsToCombineVoid.Remove(cellData.coord);
            }
            else
            {
                var rot = Quaternion.Euler(0, 60 * type.rotation, 0);
                var pos = cellData.position - transform.position;
                if (type.type == TileDictionary.EdgeTileType.WALL2_P && Random.value < 0.5f)
                {
                    var combined = generator.tiledict.CombinedMesh(TileDictionary.EdgeTileType.WALL2_P,
                        TileDictionary.EdgeTileType.DOOR);
                    wallsToCombine[cellData.coord] = MeshAsCombineInstance(combined, pos, rot, 0);
                    wallsToCombineVoid[cellData.coord] = MeshAsCombineInstance(combined, pos, rot, 1);
                }
                else
                {
                    var wallMesh = generator.tiledict.Mesh(type.type);
                    var wallSubMeshes = generator.tiledict.SubMeshs(type.type);
                    wallsToCombine[cellData.coord] = MeshAsCombineInstance(wallMesh, pos, rot, wallSubMeshes[0]);
                    wallsToCombineVoid[cellData.coord] = MeshAsCombineInstance(wallMesh, pos, rot, wallSubMeshes[1]);
                }
            }
            needsWallMeshCombining = true;
        }
        else
        {
            Debug.Log("Wall Type not found " + string.Join("|", cellData.walls));
        }
    }

    private void SpawnPickups(CellData cellData)
    {
        if (cellData.hasPickup)
        {
            var pickup = Instantiate(generator.tiledict.pickupPrefab, pickups.transform);
            pickup.transform.position = cellData.position;
        }
    }

    private void SpawnFloor(CellData cellData)
    {
        var baseMesh = generator.tiledict.baseHexMesh;
        floorsToAdd.Add(MeshAsCombineInstance(baseMesh, cellData.position - transform.position, Quaternion.identity));
    }

    private CombineInstance MeshAsCombineInstance(Mesh baseMesh, Vector3 position, Quaternion rotation, int subMesh = 0)
    {
        return new CombineInstance
        {
            mesh = baseMesh,
            transform = Matrix4x4.TRS(position, rotation, Vector3.one),
            subMeshIndex = subMesh
        };
    }
}
