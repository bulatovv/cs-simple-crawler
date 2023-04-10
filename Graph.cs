using System.Collections.Concurrent;
using System.Text;

interface IGraphNode<K, L> 
{
    K Key { get; }
    L Label { get; set; }

    IEnumerable<IGraphNode<K, L>> Neighbors { get; }
}

interface IGraph<K, L> 
{

    bool AddNode(K key, L label);

    bool AddEdge(K from, K To);

    IGraphNode<K, L>? Find(K key);

    IEnumerable<IGraphNode<K, L>> Nodes { get; }
}


class Graph<K, L>: IGraph<K, L> where K: notnull
{
    private ConcurrentDictionary<K, GraphNode> _nodeIdx;
    public IEnumerable<IGraphNode<K, L>> Nodes 
    {
        get 
        {
            return _nodeIdx.Values;
        }
    }

    private class GraphNode: IGraphNode<K, L>
    {

        public K Key { get; }
        public L Label { get; set; }
        public ConcurrentBag<GraphNode> _neighbors;

        public GraphNode(K key, L label)
        {
            Key = key;
            Label = label;
            _neighbors = new ConcurrentBag<GraphNode>();
        }
        
        public IEnumerable<IGraphNode<K, L>> Neighbors
        { 
            get { return _neighbors; } 
        }
    }

    
    public Graph()
    {
        _nodeIdx = new ConcurrentDictionary<K, GraphNode>();
    }


    public bool AddNode(K key, L label)
    {
        return _nodeIdx.TryAdd(key, new GraphNode(key, label));
    } 

    public IGraphNode<K, L>? Find(K key)
    {
        _nodeIdx.TryGetValue(key, out var result);
        return result;
    }

    public bool AddEdge(K from, K To)
    {
        var fromNode = Find(from) ?? throw new ArgumentException();
        var toNode = Find(To) ?? throw new ArgumentException();

        ((fromNode as GraphNode)!)._neighbors.Add((toNode as GraphNode)!);
        return true;
    }
}
