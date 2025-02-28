    namespace vali_deploy.Models;

    public class SubProject
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";

        public List<string> OmitFiles { get; set; } = new List<string>();
    }