# DetectPeople.Service
App has one mission - detect people on pictures. My security cameras generate tons of photos when moving detected. Sometimes move detection is triggered wrongly. So this app filters wrong moving detection - and it saves me a lot of time.
* Used [YOLOv5Net](https://github.com/mentalstack/yolov5-net) as object detector.

## How it works
In my case [HiKConsole](https://github.com/vov4uk/HikConsole) sent next message to RabbitMQ queue:
``` json
{
    "OldFilePath": "Z:\\Entrance\\192.168.0.3_01_20211203002423917_MOTION_DETECTION.jpg",
    "NewFilePath": "D:\\Cloud\\Entrance\\2021-12\\03\\00\\20211203_002423.jpg",
    "DeleteJunk": false,
    "JunkFilePath": "C:\\Junk\\Entrance\\2021-12\\03\\00\\20211203_002423.jpg",
    "NewFileName": "20211203_002423.jpg",
    "UniqueId": "18718461-48e9-495c-ae73-4170dc36d041"
}
```
DetectPeople.Service detects allowed objects (person, dog, etc.) on the picture. And put it to "NewFilePath" - if it found objects, else delete or put to "JunkFilePath".
