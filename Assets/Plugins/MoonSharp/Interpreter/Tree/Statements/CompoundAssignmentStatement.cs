using MoonSharp.Interpreter.Debugging;
using MoonSharp.Interpreter.Execution;
using MoonSharp.Interpreter.Execution.VM;
using MoonSharp.Interpreter.Tree.Expressions;

namespace MoonSharp.Interpreter.Tree.Statements
{
    class CompoundAssignmentStatement : Statement
    {
        IVariable  m_LValue;
        Expression m_RValue;
        OpCode     m_OpCode;
        SourceRef  m_Ref;

        public CompoundAssignmentStatement(ScriptLoadingContext lcontext, Expression lhsExp, Token first)
            : base(lcontext)
        {
            // 左值必须是可赋值的表达式（变量/索引）
            m_LValue = lhsExp as IVariable;
            if (m_LValue == null)
                throw new SyntaxErrorException(lcontext.Lexer.Current,
                    "unexpected symbol near '{0}' - not a l-value", lcontext.Lexer.Current);

            // 当前 token 就是 +=/-=/*=/÷= 之一
            Token opToken = lcontext.Lexer.Current;
            m_OpCode = TokenToOpCode(opToken);
            lcontext.Lexer.Next();   // 消费掉 +=/-=/*=/÷=

            // 解析右侧表达式
            m_RValue = Expression.Expr(lcontext);

            Token last = lcontext.Lexer.Current;
            m_Ref = first.GetSourceRefUpTo(last);
            lcontext.Source.Refs.Add(m_Ref);
        }

        private static OpCode TokenToOpCode(Token t)
        {
            switch (t.Type)
            {
                case TokenType.Op_AddAssign: return OpCode.Add;
                case TokenType.Op_SubAssign: return OpCode.Sub;
                case TokenType.Op_MulAssign: return OpCode.Mul;
                case TokenType.Op_DivAssign: return OpCode.Div;
                case TokenType.Op_BitAndAssign: return OpCode.BAnd;
                case TokenType.Op_BitOrAssign: return OpCode.BOr;
                default:
                    throw new InternalErrorException("Unexpected compound assignment operator '{0}'", t.Text);
            }
        }

        public override void Compile(ByteCode bc)
        {
            using (bc.EnterSource(m_Ref))
            {
                // 1. 把左值当前的值加载到栈顶
                //    SymbolRefExpression/IndexExpression 实现了 Compile() = Emit_Load
                ((Expression)m_LValue).Compile(bc);   // 注意：IVariable 同时也是 Expression

                // 2. 计算右值，压栈
                m_RValue.Compile(bc);

                // 3. 执行运算（栈：[lvalue, rvalue] → [result]）
                bc.Emit_Operator(m_OpCode);

                // 4. 把结果存回左值（从栈顶取，stackofs=0，tupleidx=0）
                m_LValue.CompileAssignment(bc, 0, 0);

                // 5. 弹掉结果（CompileAssignment 只存不弹）
                bc.Emit_Pop(1);
            }
        }
    }
}
