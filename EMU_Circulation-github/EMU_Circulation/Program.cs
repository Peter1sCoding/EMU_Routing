using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ILOG.CPLEX;
using ILOG.Concert;
using System.IO;

namespace EMU_Circulation
{
    class Program
    {    
        static void Main(string[] args)
        {
            Optimizer optimizer = new Optimizer();
            Cplex cplex = new Cplex();
            optimizer.ReadTrain(Environment.CurrentDirectory + @"/input.csv");
            optimizer.GetConnections();
            optimizer.GeneratePlans();
            optimizer.ConvertToList();
            optimizer.InitDecisionVariables(cplex);
            optimizer.SetCoverConstraints(cplex);
            optimizer.SetObj(cplex);
            optimizer.SolvePlan(cplex);
            Console.ReadKey();
        }
    }
    public class Optimizer
    {
        public List<Train> TrainList = new List<Train>();
        private Dictionary<string, Train> _trainDic;
        public List<Node> TerminateNodes = new List<Node>();
        public List<List<Train>> PlanList = new List<List<Train>>();
        public List<IIntVar> VarList = new List<IIntVar>();
        public Dictionary<string, Train> TrainDic
        {
            get
            {
                if (_trainDic == null)
                {
                    _trainDic = new Dictionary<string, Train>();
                    foreach (var train in TrainList)
                    {
                        _trainDic.Add(train.TrainNo, train);
                    }
                }
                return _trainDic;
            }
            set => _trainDic = value;
        }
        //time step: min
        public static int ConnectionTime = 20;
        public static int MaxRunTime = 48 * 60;
        public static int OneDay = 1440;
        //distance step: kilometer
        public static int MaxRunDistance = 4400;
        public void ReadTrain(string FileName)
        {
            StreamReader sr = new StreamReader(FileName, Encoding.Default);
            sr.ReadLine();
            string str = sr.ReadLine();
            while (str != null)
            {
                string[] strr = str.Split(',');
                Train train = new Train();
                train.TrainNo = strr[0];
                train.DepSta = strr[1];
                train.ArrSta = strr[2];
                train.DepTime = 60 * int.Parse(strr[3].Split(':')[0]) + int.Parse(strr[3].Split(':')[1]);
                train.ArrTime = 60 * int.Parse(strr[4].Split(':')[0]) + int.Parse(strr[4].Split(':')[1]);
                TrainList.Add(train);
                str = sr.ReadLine();
            }
            sr.Close();
        }
        public void SetObj(Cplex cplex)
        {
            ILinearIntExpr obj = cplex.LinearIntExpr();
            foreach (var var in VarList)
            {
                //obj.AddTerm(var, GetPlanConnectionTime(PlanList[VarList.IndexOf(var)]));
                obj.AddTerm(var, GetPlanDaySpan(PlanList[VarList.IndexOf(var)]));
            }
            cplex.AddMinimize(obj);
        }
        public int GetPlanDaySpan(List<Train> tempList)
        {
            int daySpan = 1;
            for (int i = 0; i < tempList.Count - 1; i++)
            {
                if (tempList[i].ArrTime > tempList[i + 1].DepTime)
                    daySpan++;
            }
            return daySpan;
        }
        public void InitDecisionVariables(Cplex cplex)
        {
            for(int i = 0; i < PlanList.Count; i++)
            {
                IIntVar var = cplex.IntVar(0, 1);
                var.Name = "x" + (i + 1).ToString();
                VarList.Add(var);
            }
        }
        public void SetCoverConstraints(Cplex cplex)
        {
            foreach(Train train in TrainList)
            {                
                ILinearNumExpr constraint = cplex.LinearNumExpr();
                for (int i = 0; i < PlanList.Count; i++)
                {
                    if (PlanList[i].Contains(train))
                    {
                        constraint.AddTerm(VarList[i], 1);
                    }
                }
                cplex.AddEq(constraint, 1, "c" + (TrainList.IndexOf(train) + 1).ToString());
            }
        }
        public void ConvertToList()
        {
            foreach (var node in TerminateNodes)
            {
                List<Train> tempTrainList = new List<Train>();
                Node tempNode = node;
                tempTrainList.Add(tempNode.TrainBelong);//将尾端加入
                for (int i = 0; i < 10; i++)
                {
                    if (tempNode.FatherNode == null)//到达头端
                        break;
                    tempNode = tempNode.FatherNode;
                    tempTrainList.Insert(0, tempNode.TrainBelong);
                }
                PlanList.Add(tempTrainList);              
            }
            List<List<Train>> addList = new List<List<Train>>();
            foreach(List<Train> tempList in PlanList)
            {
                if (tempList.Count < 3)
                    continue;
                int s = tempList.Count - 2;
                for(int m = 0; m < tempList.Count - 2; m++)
                {
                    List<Train> list = new List<Train>();
                    for (int i = s; i >= 0; i--)
                    {
                        list.Insert(0, tempList[i]);
                    }
                    addList.Add(list);
                    s--;
                }
            }
            PlanList.AddRange(addList);
            List<List<Train>> tempPlanList = new List<List<Train>>();
            foreach (var plan in PlanList)
            {
                if (plan.First().DepSta == plan.Last().ArrSta)
                    tempPlanList.Add(plan);
            }
            PlanList = tempPlanList;
            //foreach (List<Train> tempList in PlanList)
            //{
            //    int length = 0;
            //    int time = 0;
            //    for (int i = 0; i < tempList.Count; i++)
            //    {
            //        length += tempList[i].RunDistance;
            //        time += tempList[i].RunTime;
            //        if(i == 0)
            //                                Console.Write((PlanList.IndexOf(tempList) + 1) + ":  ");
            //        if (i == tempList.Count - 1)
            //            Console.WriteLine(tempList[i].TrainNo + " Length:" + length + " time:" + time + " day: " + GetPlanDaySpan(tempList));
            //        else
            //            Console.Write(tempList[i].TrainNo + ",");
            //    }
            //}
        }
        public void GeneratePlans()
        {
            foreach(Train train in TrainList)
            {
                Node oriNode = new Node(train);
                oriNode.AccumLength = train.RunDistance;
                oriNode.AccumTime = train.RunTime;
                oriNode.IsFirst = true;
                oriNode.DepotBelong = train.ArrSta;
                train.HeadNode = oriNode;
            }
            List<Node> curNodeList = new List<Node>();
            List<Node> nextNodeList = new List<Node>();
            for (int i = 0; i < 10; i++)
            {
                if (i == 0)
                {
                    foreach (var train in TrainList)
                        curNodeList.Add(train.HeadNode);
                }
                else
                {
                    foreach (var node in curNodeList)
                    {  
                        if (node.SonNodes.Count == 0)
                            TerminateNodes.Add(node);
                        foreach (var sonNode in node.SonNodes)
                            nextNodeList.Add(sonNode);
                    }
                    curNodeList = nextNodeList;
                    nextNodeList = new List<Node>();
                }
                foreach (var node in curNodeList)
                    GenerateNodes(node);
            }
        }
        public void GenerateNodes(Node fatherNode)
        {
            if (fatherNode.IsFirst)//为首车
            {
                fatherNode.AccumLength = fatherNode.TrainBelong.RunDistance;
                fatherNode.AccumTime = fatherNode.TrainBelong.RunTime;
            }                
            foreach(Train tra in fatherNode.TrainBelong.ConnectTrains)
            {
                Node node = new Node(tra);
                if (fatherNode.TrainBelong.ArrTime > tra.DepTime - 20)//若出现检修
                {
                    //if (tra.DepSta != fatherNode.DepotBelong)//只在配属站检修
                    //    continue;
                    node.AccumTime = tra.RunTime;
                    node.AccumLength = tra.RunDistance;
                    node.DayBelong = fatherNode.DayBelong + 1;                   
                }
                else
                {
                    node.AccumTime = fatherNode.AccumTime + tra.RunTime + GetConnectionTime(fatherNode.TrainBelong, tra);
                    node.AccumLength = fatherNode.AccumLength + tra.RunDistance;
                    node.DayBelong = fatherNode.DayBelong;
                }
                if (node.DayBelong > 2)
                    continue;
                //if (fatherNode.AccumLength + tra.RunDistance > MaxRunDistance || fatherNode.AccumTime + tra.RunTime + GetConnectionTime(fatherNode.TrainBelong, tra) > MaxRunTime)
                //    continue;
                if (fatherNode.AccumLength + tra.RunDistance > MaxRunDistance)
                    continue;
                node.FatherNode = fatherNode;
                fatherNode.SonNodes.Add(node);
            }
        }
        public void GetConnections()
        {
            foreach (var preTrain in TrainList)
            {
                foreach (var nextTrain in TrainList)
                {
                    if (preTrain.Equals(nextTrain))
                        continue;

                    //if (nextTrain.DepSta == preTrain.ArrSta && nextTrain.DepTime >= preTrain.ArrTime + 20)
                    //    preTrain.ConnectTrains.Add(nextTrain);
                    if (nextTrain.DepSta == preTrain.ArrSta)
                        preTrain.ConnectTrains.Add(nextTrain);
                }
            }
        }
        public int GetPlanConnectionTime(List<Train> plan)
        {
            int a = 0;
            for (int i = 0; i < plan.Count - 1; i++)
            {
                a += GetConnectionTime(plan[i], plan[i + 1]);
            }
            a += GetConnectionTime(plan.Last(), plan.First());
            return a;
            
        }
        public int GetConnectionTime(Train preTrain, Train nextTrain)
        {
            if (preTrain.ArrTime <= nextTrain.DepTime - 20)
                return nextTrain.DepTime - preTrain.ArrTime;
            else
                return preTrain.ArrTime + OneDay - nextTrain.DepTime;
        }
        public void SolvePlan(Cplex cplex)
        {
            try
            {
                cplex.SetParam(Cplex.DoubleParam.EpGap, 0.03);
                Console.WriteLine("Start");

                if (cplex.Solve())
                {
                    Console.WriteLine("Solved Successfully!");
                    int num = 0;
                    foreach (var item in VarList)
                    {
                        num += GetPlanDaySpan(PlanList[VarList.IndexOf(item)]) * (int)cplex.GetValue(item);
                    }

                    Console.WriteLine("共需动车组" + num + "辆");
                    for (int i = 0; i < PlanList.Count; i++)
                    {
                        if ((int)cplex.GetValue(VarList[i]) == 0)
                            continue;
                        List<Train> tempList = PlanList[i];
                        int length = 0;
                        int time = 0;
                        for (int j = 0; j < tempList.Count; j++)
                        {
                            length += tempList[j].RunDistance;
                            time += tempList[j].RunTime;

                            if (j == tempList.Count - 1)
                                Console.WriteLine(tempList[j].TrainNo + " Length:" + length + " time:" + time);
                            else
                                Console.Write(tempList[j].TrainNo + ",");
                        }
                    }
                }
                else
                {
                    Cplex.Status status = cplex.GetStatus();
                    Console.WriteLine(status);
                    cplex.ExportModel("C:\\data\\m.lp");
                    Console.ReadKey();
                }
            }
            catch (ILOG.Concert.Exception ex)
            {
                System.Console.WriteLine("Concert Error: " + ex);
            }
            catch (System.IO.IOException ex)
            {
                System.Console.WriteLine("IO Error: " + ex);
            }
        }
        public class Node
        {
            public Node(Train trainBelong) { this.TrainBelong = trainBelong; }
            public Node(Node fatherNode, Train trainBelong)
            {
                FatherNode = fatherNode;
                TrainBelong = trainBelong;
                DepotBelong = fatherNode.DepotBelong;
            }
            public bool IsFirst = false;
            public Node FatherNode;
            public List<Node> SonNodes = new List<Node>();
            public Train TrainBelong;
            public int AccumTime = 0;
            public int AccumLength = 0;
            public int DayBelong = 1;
            public string DepotBelong = "";
            public override string ToString()
            {
                return TrainBelong.TrainNo;
            }
        }
        public class Train
        {
            public string TrainNo;
            public string ArrSta;
            public string DepSta;
            public int ArrTime;
            public int DepTime;
            public Node HeadNode;

            public int RunTime
            {
                get => ArrTime - DepTime;
            }

            private List<Train> _connectTrains;
            public List<Train> ConnectTrains
            {
                get
                {
                    if (_connectTrains == null)
                        _connectTrains = new List<Train>();
                    return _connectTrains;
                }
                set => _connectTrains = value;
            }
            public int RunDistance
            {
                get
                {
                    if ((DepSta == "A" && ArrSta == "B") || (DepSta == "B" && ArrSta == "A"))
                        return 400;
                    else if ((DepSta == "A" && ArrSta == "C") || (DepSta == "C" && ArrSta == "A"))
                        return 1300;
                    else if ((DepSta == "B" && ArrSta == "C") || (DepSta == "C" && ArrSta == "B"))
                        return 900;
                    return 10000;
                }
            }
            public override string ToString() => TrainNo + ":" + DepSta + "-" + ArrSta;
        }
    }   
}
