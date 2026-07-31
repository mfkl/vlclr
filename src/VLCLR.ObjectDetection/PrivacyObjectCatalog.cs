namespace VLCLR.ObjectDetection;

public static class PrivacyObjectCatalog
{
    public const int FaceClassId = 80;
    public const int LicensePlateClassId = 81;

    public static ObjectClassDescriptor Face { get; } = new(
        FaceClassId,
        "face",
        "Face",
        ["faces"]);

    public static ObjectClassDescriptor LicensePlate { get; } = new(
        LicensePlateClassId,
        "license plate",
        "License plate",
        ["license plates", "plate", "plates", "number plate"]);

    public static ObjectClassCatalog Create()
    {
        var classes = new List<ObjectClassDescriptor>(82);
        classes.AddRange(Coco80ObjectCatalog.Create().Classes);
        classes.Add(Face);
        classes.Add(LicensePlate);
        return new ObjectClassCatalog(classes);
    }
}
