using Newtonsoft.Json;

namespace DetectPeople.Contracts.Message
{
    public class DetectPeopleMessage
    {
        public bool DeleteJunk { get; set; }

        public string JunkFilePath { get; set; }

        public string NewFileName { get; set; }

        public string NewFilePath { get; set; }

        public string OldFilePath { get; set; }

        public string UniqueId { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
