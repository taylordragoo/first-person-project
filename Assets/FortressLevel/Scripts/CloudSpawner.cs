//Copyright 2025 Kinemation

using UnityEngine;
using System.Collections;

public class CloudSpawner : MonoBehaviour
{
    public GameObject cloudPrefab;
    public int maxClouds = 20;
    public float spawnRadius = 200f;
    public float spawnHeight = 100f;
    public float spawnInterval = 5f;
    public int prewarmCount = 10;

    public Vector2 scaleRange = new Vector2(0.8f, 2.5f);
    public Vector2 stretchRange = new Vector2(1f, 3f);
    public Vector2 speedRange = new Vector2(0.5f, 2f);
    public Vector2 lifetimeRange = new Vector2(30f, 90f);

    public float layerHeightOffset = 10f;
    public float rotationRange = 360f;

    [Range(0f, 1f)] public float lowCloudChance = 0.2f;
    public float lowCloudHeight = 20f;
    public float lowCloudTallnessMultiplier = 3f;

    private int currentCloudCount;

    private void Start()
    {
        int count = Mathf.Min(prewarmCount, maxClouds);
        for (int i = 0; i < count; i++)
            SpawnCloud(true);

        StartCoroutine(SpawnCloudsRoutine());
    }

    private IEnumerator SpawnCloudsRoutine()
    {
        while (true)
        {
            if (currentCloudCount < maxClouds)
                SpawnCloud();
            yield return new WaitForSeconds(spawnInterval);
        }
    }

    private void SpawnCloud(bool prewarm = false)
    {
        bool isLowCloud = Random.value < lowCloudChance;
        float height = isLowCloud
            ? lowCloudHeight + Random.Range(-5f, 5f)
            : spawnHeight + Random.Range(-layerHeightOffset, layerHeightOffset);

        Vector3 spawnPos = transform.position + new Vector3(
            Random.Range(-spawnRadius, spawnRadius),
            height,
            Random.Range(-spawnRadius, spawnRadius)
        );

        GameObject cloud = Instantiate(
            cloudPrefab,
            spawnPos,
            Quaternion.Euler(0f, Random.Range(0f, rotationRange), 0f)
        );

        Cloud cloudComp = cloud.AddComponent<Cloud>();

        float baseScale = Random.Range(scaleRange.x, scaleRange.y);
        float stretch = Random.Range(stretchRange.x, stretchRange.y);

        float horizontalMultiplier = 1f;
        if (!isLowCloud)
            horizontalMultiplier = Mathf.Lerp(1f, 2f, (height - spawnHeight) / (layerHeightOffset * 2f));

        Vector3 finalScale = new Vector3(baseScale * stretch * horizontalMultiplier, baseScale, baseScale * horizontalMultiplier);

        if (isLowCloud)
            finalScale.y *= lowCloudTallnessMultiplier;

        cloud.transform.localScale = finalScale;

        float moveSpeed = Random.Range(speedRange.x, speedRange.y);
        float lifetime = Random.Range(lifetimeRange.x, lifetimeRange.y);

        cloudComp.Initialize(this, moveSpeed, lifetime, prewarm, isLowCloud);
        currentCloudCount++;
    }

    public void OnCloudDestroyed()
    {
        currentCloudCount--;
    }
}
