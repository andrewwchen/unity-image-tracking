using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Linq;
using TMPro;

[RequireComponent(typeof(ARTrackedImageManager))]
public class ForestSceneManager : MonoBehaviour
{

    [SerializeField] private ForestScene[] forestScenes;
	[Tooltip("The ForestScene will be positioned based on the most recently added tracked image or the most recently tracked image within maxRetrackDistance in the camera view")]
	[SerializeField] private float maxRetrackDistance = 2.0f;
	[Tooltip("Text box in which to show debug information.")]
	[SerializeField] private TMP_Text debugText;

	private ARTrackedImageManager arTrackedImageManager;

	// Dictionary<image name : ForestScene>
	// maps image target names to the corresponding Forest Scene
	private Dictionary<string, ForestScene> imageToScene = new Dictionary<string, ForestScene>();

	// Dictionary<image name : ARTrackedImage>
	// contains every image target that ARFoundation is currently tracking
	// maps image target name to the corresponding ARTrackedImage object
	// image targets can have a TrackingState of Limited or Tracking
	private Dictionary<string, ARTrackedImage> trackedImages = new Dictionary<string, ARTrackedImage>();

	// contains every image target that ARFoundation is currently tracking with the TrackingState of Tracking
	// ordered by time last tracked
	private List<string> trackedImagesOrder = new List<string>();

	// contains every image target that ARFoundation is currently tracking with the TrackingState of Limited
	// ordered by time last tracked
	private List<string> limitedImagesOrder = new List<string>();

	// the name of the image target that is being used to position and orient the currentForestScene 
	private string currentImageName = "";

	// the ForestScene currently being displayed which corresponds to currentImageName
	private ForestScene currentForestScene;


	private List<string> pointedImages = new List<string>();

	private void Awake()
	{
		arTrackedImageManager = GetComponent<ARTrackedImageManager>();
	}

    private void Start()
    {
		foreach (ForestScene fs in forestScenes)
		{
			// hide all ForestScenes at the start
			fs.gameObject.SetActive(false);

			// associate each image target with their corresponding scene
			foreach (string img in fs.imageToTransform.Keys)
			{
				imageToScene[img] = fs;
			}
		}
	}

    void OnEnable() => arTrackedImageManager.trackedImagesChanged += OnChanged;

    void OnDisable() => arTrackedImageManager.trackedImagesChanged -= OnChanged;

	// event handler for updating the tracking status of every image target
	void OnChanged(ARTrackedImagesChangedEventArgs eventArgs)
	{
		foreach (ARTrackedImage newImage in eventArgs.added)
		{
			UpdateImages(newImage);
		}

		foreach (ARTrackedImage updatedImage in eventArgs.updated)
		{
			UpdateImages(updatedImage);
		}

		foreach (ARTrackedImage removedImage in eventArgs.removed)
		{
			UpdateImages(removedImage);
		}
	}

	// function bound to the OnChanged event handler
	private void UpdateImages(ARTrackedImage image, bool retrack=false)
	{
		string imageName = image.referenceImage.name;
		switch (image.trackingState)
		{
			case TrackingState.Tracking:
				trackedImages[imageName] = image;
				trackedImagesOrder.Remove(imageName);
				trackedImagesOrder.Add(imageName);
				limitedImagesOrder.Remove(imageName);
				break;
			case TrackingState.Limited:
				// filter out duplicate limited tracking events
				if (!limitedImagesOrder.Contains(imageName) || retrack) {
					trackedImages[imageName] = image;
					limitedImagesOrder.Remove(imageName);
					limitedImagesOrder.Add(imageName);
					trackedImagesOrder.Remove(imageName);
				}
				break;
			default: // TrackingState.None
				trackedImages.Remove(imageName);
				trackedImagesOrder.Remove(imageName);
				limitedImagesOrder.Remove(imageName);
				break;
		}
	}

	// refreshes the tracking status of tracked images currently in the center of the viewport within maxRetrackDistance
	private void UpdateViewedImages()
    {
		// test if the camera is currently pointed at a tracked image
		// if so, set that image to the top of its limited or tracked order
		Vector3 camDirection = Camera.main.transform.forward;
		float minZ = maxRetrackDistance;
		ARTrackedImage pointedImage = null;

		pointedImages.Clear();
		foreach (string n in trackedImages.Keys)
		{
			Transform t = trackedImages[n].transform;
			Vector3 imgDirection = t.up;
			Vector3 p = Camera.main.WorldToViewportPoint(t.position);
			// check if the image center is within the viewport and close enough to the camera and facing the camera
			if (p.x > 0 && p.x < 1 && p.y > 0 && p.y < 1 && p.z > 0 && p.z < maxRetrackDistance && Vector3.Angle(camDirection, imgDirection) > 120)
			{
				pointedImages.Add(n);
				if (p.z < minZ)
                {
					minZ = p.z;
					pointedImage = trackedImages[n];
				}
			}
		}

		if (pointedImage != null)
		{
			UpdateImages(pointedImage, true);
		}
	}

	private void Update()
	{
		// refresh the tracking status of images in the center of the viewport
		UpdateViewedImages();

		// determine the name and transform of the currently tracked image
		string imageName;
		Transform imageTransform;
		if (trackedImagesOrder.Count > 0) // if we are actively tracking at least one image, use the most recently tracked image
		{
			imageName = trackedImagesOrder.Last();
			imageTransform = trackedImages[imageName].transform;
		}
		else if (limitedImagesOrder.Count > 0) // if we are not actively tracking an image, use the most recently tracked image
		{
			imageName = limitedImagesOrder.Last();
			imageTransform = trackedImages[imageName].transform;
		}
		else
		{
			if (currentForestScene != null)
			{
				currentForestScene.gameObject.SetActive(false);
			}
			return;
		}

		// TODO: check if the current image is too far, in which case hide the forest scene
		if (false)
        {
			currentForestScene.gameObject.SetActive(false);
		}
		// otherwise change the Forest Scene position, orientation, and/or the Forest Scene itself if a different image is being tracked since the last update
		else if (currentImageName != imageName)
        {
			currentImageName = imageName;

			// activate the forest scene corresponding to the currently tracked image
			ForestScene newForestScene = imageToScene[imageName];
			if (newForestScene != currentForestScene)
			{
				if (currentForestScene != null)
				{
					currentForestScene.transform.parent = null;
					currentForestScene.gameObject.SetActive(false);
				}
				currentForestScene = newForestScene;
				currentForestScene.gameObject.SetActive(true);
			}

			// set the position and rotation of the forest scene relative to the currentImage's corresponding placeholder's location within the forestscene
			Quaternion sceneRotation = currentForestScene.imageToTransform[imageName].localRotation;
			Quaternion worldRotation = imageTransform.rotation;
			Vector3 scenePosition = currentForestScene.imageToTransform[imageName].localPosition;
			Vector3 worldPosition = imageTransform.position;

			Quaternion sceneToWorldRotation = worldRotation * Quaternion.Inverse(sceneRotation);

			Vector3 rotatedScenePosition = sceneToWorldRotation * scenePosition;

			currentForestScene.transform.position = worldPosition - rotatedScenePosition;
			currentForestScene.transform.rotation = sceneToWorldRotation;
			currentForestScene.transform.parent = imageTransform;
		}

		// display debug info in the text box
		if (debugText != null)
		{
			debugText.text = "Tracked Images: " + string.Join(", ", trackedImagesOrder) + "\n Limited Images: " + string.Join(", ", limitedImagesOrder) + "\n Pointed Images: " + string.Join(", ", pointedImages) + "\n Current Image: " + imageName;
		}
	}
}
