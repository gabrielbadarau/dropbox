import { useAuth } from "../lib/auth";

export default function Dashboard() {
  const { user, logout } = useAuth();

  return (
    <div className="p-8">
      <h1 className="text-xl font-semibold">Dashboard</h1>
      <p className="mt-2 text-sm text-neutral-500">
        Signed in as {user?.email}
      </p>
      <button
        onClick={logout}
        className="mt-4 rounded-lg border border-neutral-300 px-3 py-1.5 text-sm dark:border-neutral-700"
      >
        Log out
      </button>
    </div>
  );
}
