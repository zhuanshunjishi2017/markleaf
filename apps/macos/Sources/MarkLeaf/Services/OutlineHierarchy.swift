import Foundation

final class OutlineNode {
    let heading: OutlineHeading
    var children: [OutlineNode] = []

    init(heading: OutlineHeading) {
        self.heading = heading
    }
}

enum OutlineHierarchy {
    static func makeNodes(_ headings: [OutlineHeading]) -> [OutlineNode] {
        var roots: [OutlineNode] = []
        var stack: [OutlineNode] = []
        for heading in headings {
            let node = OutlineNode(heading: heading)
            while let last = stack.last, last.heading.level >= heading.level {
                stack.removeLast()
            }
            if let parent = stack.last {
                parent.children.append(node)
            } else {
                roots.append(node)
            }
            stack.append(node)
        }
        return roots
    }

    static func flatten(_ nodes: [OutlineNode]) -> [OutlineNode] {
        nodes.flatMap { [$0] + flatten($0.children) }
    }
}
