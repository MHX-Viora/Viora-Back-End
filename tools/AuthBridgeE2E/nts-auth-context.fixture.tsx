// Use the production auth state; only unrelated push/chat/storage effects are
// replaced by local fixture adapters in the bundler.
export {AuthProvider as AuthFixture,useAuth} from '../../../Digital-Creative/Digital_Creative_App/store/auth-context';
