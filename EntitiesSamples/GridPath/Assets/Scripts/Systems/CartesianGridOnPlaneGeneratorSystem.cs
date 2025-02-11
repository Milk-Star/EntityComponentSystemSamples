using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[RequireMatchingQueriesForUpdate]
public unsafe partial class CartesianGridOnPlaneSystemGeneratorSystem : SystemBase
{
    protected override void OnUpdate()
    {
        Dependency.Complete();
        Entities.WithStructuralChanges().ForEach((Entity entity, in CartesianGridOnPlaneGenerator cartesianGridOnPlaneGenerator, in DynamicBuffer<CartesianGridOnPlaneGeneratorFloorPrefab> floorPrefabsBuffer) =>
        {
            var floorPrefabs = new NativeArray<Entity>(floorPrefabsBuffer.Length, Allocator.Temp);
            floorPrefabs.CopyFrom(floorPrefabsBuffer.Reinterpret<Entity>().AsNativeArray());

            var wallPrefab = cartesianGridOnPlaneGenerator.WallPrefab;
            var rowCount = cartesianGridOnPlaneGenerator.RowCount;
            var colCount = cartesianGridOnPlaneGenerator.ColCount;
            var wallSProbability = cartesianGridOnPlaneGenerator.WallSProbability;
            var wallWProbability = cartesianGridOnPlaneGenerator.WallWProbability;

            var floorPrefabCount = floorPrefabsBuffer.Length;
            if (floorPrefabCount == 0)
                return;

            var cx = (colCount * 0.5f);
            var cz = (rowCount * 0.5f);

            // 4 bits per grid section (bit:0=N,1=S,2=W,3=E)
            var gridWallsSize = (rowCount * (colCount + 1) / 2);

            var blobBuilder = new BlobBuilder(Allocator.Temp);
            ref var cartesianGridOnPlaneBlob = ref blobBuilder.ConstructRoot<CartesianGridOnPlaneBlob>();

            var trailingOffsets = blobBuilder.Allocate(ref cartesianGridOnPlaneBlob.TrailingOffsets, 4);
            var gridWalls = blobBuilder.Allocate(ref cartesianGridOnPlaneBlob.Walls, gridWallsSize);

            cartesianGridOnPlaneBlob.RowCount = (ushort)rowCount;
            cartesianGridOnPlaneBlob.ColCount = (ushort)colCount;

            CartesianGridGeneratorUtility.CreateGridPath(rowCount, colCount, (byte*)gridWalls.GetUnsafePtr(), wallSProbability, wallWProbability, true);

            // Create visible geometry
            for (int y = 0; y < rowCount; y++)
                for (int x = 0; x < colCount; x++)
                {
                    var prefabIndex = (x + y) % floorPrefabCount;
                    var tx = ((float)x) - cx;
                    var tz = ((float)y) - cz;

                    CartesianGridGeneratorUtility.CreateFloorPanel(EntityManager, floorPrefabs[prefabIndex], float4x4.identity, tx, tz);

                    var gridWallsIndex = (y * ((colCount + 1) / 2)) + (x / 2);
                    var walls = (gridWalls[gridWallsIndex] >> ((x & 1) * 4)) & 0x0f;

                    if ((walls & 0x02) != 0) // South wall
                        CartesianGridGeneratorUtility.CreateWallS(EntityManager, wallPrefab, float4x4.identity, tx, tz);
                    if ((walls & 0x04) != 0) // West wall
                        CartesianGridGeneratorUtility.CreateWallW(EntityManager, wallPrefab, float4x4.identity, tx, tz);
                    if (y == (rowCount - 1)) // North wall
                        CartesianGridGeneratorUtility.CreateWallS(EntityManager, wallPrefab, float4x4.identity, tx, tz + 1.0f);
                    if (x == (colCount - 1)) // East wall
                        CartesianGridGeneratorUtility.CreateWallW(EntityManager, wallPrefab, float4x4.identity, tx + 1.0f, tz);
                }


            trailingOffsets[0] = new float2(cx + 0.0f, cz + -0.5f); // North
            trailingOffsets[1] = new float2(cx + 0.0f, cz + 0.5f); // South
            trailingOffsets[2] = new float2(cx + 0.5f, cz + 0.0f); // West
            trailingOffsets[3] = new float2(cx + -0.5f, cz + 0.0f); // East

            EntityManager.AddComponentData(entity, new CartesianGridOnPlane
            {
                Blob = blobBuilder.CreateBlobAssetReference<CartesianGridOnPlaneBlob>(Allocator.Persistent)
            });

            blobBuilder.Dispose();

            EntityManager.RemoveComponent<CartesianGridOnPlaneGenerator>(entity);
        }).Run();
    }
}
