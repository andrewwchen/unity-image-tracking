# Image Tracking Pipeline for Unity Mobile AR

![IMG_0087](https://github.com/chenmasterandrew/entangledecologies/assets/30731383/976728c3-8f04-4a7a-b98b-36af0a6d0fc3)

## Dependencies
1. AR Foundation Version 4.2.7 for the ARCameraBackground component used to access the device's primary camera feed

## Setup
1. Create a Unity project with AR Foundation Version 4.2.7
2. Download ImageRecognition.unitypackage from this repository and import the package into your Unity project
3. Add the ImageRecognitionManager component to your scene
4. Create an ARCameraBackground component on your ARCamera
5. Link the ARCameraBackground component to the ImageRecognitionManager component
6. Link the Camera component on the ARCamera to the ImageRecognitionManager component under "Primary Camera"
7. Optionally Link a second Camera component for use in Unity edit mode to the to the ImageRecognitionManager component under "Secondary Camera"
8. Create an XR Reference Image Library, link it to the ImageRecognitionManager, and populate it with the names, images, and real sizes of each image you wish to track (images of 256 x 256 pixels recommended)

Alternatively, use the SampleScene in this repository.

## Usage
Once Setup is completed, you can subscribe to the imageRecognitionEvent on the ImageRecognitionManager, which fires whenever a target is detected in the camera feed. It additionally provides a struct detailing information about the instance of image recognition. See the DebugCanvas.cs script for an example on how to handle this event.

