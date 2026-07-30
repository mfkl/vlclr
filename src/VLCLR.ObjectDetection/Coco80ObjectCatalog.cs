namespace VLCLR.ObjectDetection;

public static class Coco80ObjectCatalog
{
    private static readonly string[] Labels =
    [
        "person",
        "bicycle",
        "car",
        "motorcycle",
        "airplane",
        "bus",
        "train",
        "truck",
        "boat",
        "traffic light",
        "fire hydrant",
        "stop sign",
        "parking meter",
        "bench",
        "bird",
        "cat",
        "dog",
        "horse",
        "sheep",
        "cow",
        "elephant",
        "bear",
        "zebra",
        "giraffe",
        "backpack",
        "umbrella",
        "handbag",
        "tie",
        "suitcase",
        "frisbee",
        "skis",
        "snowboard",
        "sports ball",
        "kite",
        "baseball bat",
        "baseball glove",
        "skateboard",
        "surfboard",
        "tennis racket",
        "bottle",
        "wine glass",
        "cup",
        "fork",
        "knife",
        "spoon",
        "bowl",
        "banana",
        "apple",
        "sandwich",
        "orange",
        "broccoli",
        "carrot",
        "hot dog",
        "pizza",
        "donut",
        "cake",
        "chair",
        "couch",
        "potted plant",
        "bed",
        "dining table",
        "toilet",
        "tv",
        "laptop",
        "mouse",
        "remote",
        "keyboard",
        "cell phone",
        "microwave",
        "oven",
        "toaster",
        "sink",
        "refrigerator",
        "book",
        "clock",
        "vase",
        "scissors",
        "teddy bear",
        "hair drier",
        "toothbrush"
    ];

    private static readonly IReadOnlyDictionary<string, string[]> Aliases =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["person"] = ["people", "human"],
            ["bicycle"] = ["bike"],
            ["motorcycle"] = ["motorbike"],
            ["airplane"] = ["plane", "aeroplane"],
            ["sports ball"] = ["ball", "sportsball"],
            ["couch"] = ["sofa"],
            ["potted plant"] = ["plant"],
            ["dining table"] = ["table"],
            ["tv"] = ["television", "television screen"],
            ["cell phone"] = ["phone", "mobile phone", "cellphone"],
            ["refrigerator"] = ["fridge"],
            ["hair drier"] = ["hair dryer"]
        };

    public static ObjectClassCatalog Create()
    {
        ObjectClassDescriptor[] classes = Labels
            .Select((label, id) => new ObjectClassDescriptor(
                id,
                label,
                label,
                Aliases.TryGetValue(label, out string[]? aliases)
                    ? Array.AsReadOnly(aliases)
                    : Array.Empty<string>()))
            .ToArray();
        return new ObjectClassCatalog(classes);
    }
}
