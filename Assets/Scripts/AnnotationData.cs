using UnityEngine;

[CreateAssetMenu(fileName = "AnnotationData", menuName = "ScriptableObjects/AnnotationData")]
public class AnnotationData : ScriptableObject
{
    public string title;
    public string description;
    public AudioClip audio;
    public Sprite[] images;
}