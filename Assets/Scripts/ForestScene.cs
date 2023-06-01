using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ForestScene : MonoBehaviour
{
	[HideInInspector] public Dictionary<string, Transform> imageToTransform = new Dictionary<string, Transform>();

    [SerializeField] private string[] imageNames;
    [SerializeField] private Transform[] imageTransforms;

    private void Awake()
    {
		imageToTransform = Utils.Zip(imageNames, imageTransforms);
	}
}
