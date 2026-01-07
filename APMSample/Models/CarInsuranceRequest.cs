namespace APMSample.Models
{
    public class CarInsuranceRequest
    {
        public string CarOwnerName { get; set; } // 車主姓名
        public string CarPlate { get; set; }     // 車牌
        public int CarAge { get; set; }          // 車齡
        public decimal CarValue { get; set; }    // 重置價格
        public string CoverageType { get; set; } // 險種 (甲式/乙式/丙式)
    }
}
